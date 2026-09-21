using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Adb;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbPluginOperationsTests
{
	private const string Serial = "R58M12ABCDE";

	private static readonly string LocalFile = OperatingSystem.IsWindows() ? @"C:\data\app.apk" : "/data/app.apk";

	[Test]
	public void A_shell_command_reaches_the_device_shell_as_one_argument()
	{
		var built = AdbCommandBuilder.BuildPluginShell(Serial, "dumpsys battery | grep level");

		Assert.That(built.Data, Is.EqualTo(new[] { "-s", Serial, "shell", "dumpsys battery | grep level" }));
	}

	[TestCase("")]
	[TestCase("   ")]
	[TestCase("-x ls")]
	[TestCase("  -t cat")]
	[TestCase("echo\0hidden")]
	public void A_shell_command_adb_would_misread_or_that_is_empty_is_refused(string command)
		=> Assert.That(AdbCommandBuilder.BuildPluginShell(Serial, command).Error, Is.EqualTo(AdbFailureCode.InvalidParameter));

	[Test]
	public void An_empty_serial_is_refused_rather_than_falling_back_to_a_default_device()
		=> Assert.That(AdbCommandBuilder.BuildPluginShell(string.Empty, "true").Error, Is.EqualTo(AdbFailureCode.InvalidParameter));

	[Test]
	public void Install_replaces_an_existing_app_and_passes_the_apk_as_its_own_argument()
		=> Assert.That(AdbCommandBuilder.BuildInstall(Serial, LocalFile).Data,
			Is.EqualTo(new[] { "-s", Serial, "install", "-r", LocalFile }));

	[TestCase("app.apk")]
	[TestCase("-r")]
	[TestCase("../app.apk")]
	public void A_local_path_that_is_not_absolute_is_refused(string path)
		=> Assert.That(AdbCommandBuilder.BuildInstall(Serial, path).Error, Is.EqualTo(AdbFailureCode.InvalidParameter));

	[TestCase("sdcard/file.txt")]
	[TestCase("-a")]
	[TestCase("/sdcard/a\nb")]
	public void A_device_path_that_is_not_absolute_is_refused(string remotePath)
		=> Assert.That(AdbCommandBuilder.BuildPush(Serial, LocalFile, remotePath).Error,
			Is.EqualTo(AdbFailureCode.InvalidParameter));

	[TestCase("com.example.app; reboot")]
	[TestCase("-k")]
	public void A_package_name_outside_the_package_grammar_is_refused(string packageName)
		=> Assert.That(AdbCommandBuilder.BuildUninstall(Serial, packageName).Error, Is.EqualTo(AdbFailureCode.InvalidParameter));

	[Test]
	public void A_charging_battery_is_normalised()
	{
		var reading = AdbPropertyParsers.ParseBatteryState(
			"Current Battery Service state:\n  AC powered: false\n  USB powered: true\n  Wireless powered: false\n" +
			"  status: 2\n  health: 2\n  present: true\n  level: 87\n  scale: 100\n");

		Assert.That(reading, Is.EqualTo(new AdbBatteryReading(87, true, AdbBatteryStatus.Charging, AdbBatteryHealth.Good)));
	}

	[Test]
	public void A_full_battery_on_the_charger_still_reports_that_power_is_connected()
	{
		var reading = AdbPropertyParsers.ParseBatteryState("  AC powered: true\n  status: 5\n  health: 3\n  level: 100\n");

		Assert.That(reading, Is.EqualTo(new AdbBatteryReading(100, true, AdbBatteryStatus.Full, AdbBatteryHealth.Overheat)));
	}

	[Test]
	public void A_level_on_another_scale_is_converted_to_percent()
	{
		var reading = AdbPropertyParsers.ParseBatteryState("  AC powered: false\n  status: 3\n  health: 7\n  level: 50\n  scale: 200\n");

		Assert.That(reading, Is.EqualTo(new AdbBatteryReading(25, false, AdbBatteryStatus.Discharging, AdbBatteryHealth.Cold)));
	}

	[Test]
	public void Output_without_a_level_is_not_a_battery_reading()
		=> Assert.That(AdbPropertyParsers.ParseBatteryState("sh: dumpsys: not found\n"), Is.Null);

	[Test]
	public async Task A_non_zero_exit_code_from_a_shell_command_is_a_result_not_a_failure()
	{
		using var harness = await OnlineHarnessAsync();
		harness.Runner.When(IsShell, new AdbProcessResult(true, 3, "partial", "boom", false));

		var result = await harness.Manager.RunShellAsync(Serial, "false", CancellationToken.None);

		Assert.That(result.Data, Is.EqualTo(new AdbShellOutput(3, "partial", "boom", false)));
	}

	[Test]
	public async Task Shell_output_beyond_the_limit_is_cut_and_reported_as_truncated()
	{
		using var harness = await OnlineHarnessAsync();
		harness.Runner.When(IsShell, new AdbProcessResult(true, 0, new string('x', 200_000), string.Empty, false));

		var result = await harness.Manager.RunShellAsync(Serial, "logcat -d", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Truncated, Is.True);
			Assert.That(result.Data.StandardOutput.Length, Is.LessThan(200_000));
		});
	}

	[Test]
	public async Task An_operation_on_a_device_that_is_not_online_fails_with_the_device_state_and_runs_nothing()
	{
		using var harness = await OnlineHarnessAsync("unauthorized");
		var before = harness.Runner.Invocations.Count;

		var result = await harness.Manager.RunShellAsync(Serial, "true", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(AdbFailureCode.DeviceUnauthorized));
			Assert.That(harness.Runner.Invocations, Has.Count.EqualTo(before));
		});
	}

	[Test]
	public async Task Every_operation_reports_Disabled_while_adb_is_switched_off()
	{
		using var harness = new AdbManagerHarness();
		harness.PreferenceService.AdbSettings = new AdbSettings(false, "fake-adb", true, null);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var result = await harness.Manager.IsPackageInstalledAsync(Serial, "com.example.app", CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(AdbFailureCode.Disabled));
	}

	[TestCase("package:/data/app/com.example.app-1/base.apk\n", 0, true)]
	[TestCase("", 1, false)]
	[TestCase("", 0, false)]
	public async Task A_package_counts_as_installed_only_when_pm_names_its_path(string output, int exitCode, bool expected)
	{
		using var harness = await OnlineHarnessAsync();
		harness.Runner.When(argv => IsShell(argv) && argv[3].StartsWith("pm path", StringComparison.Ordinal),
			new AdbProcessResult(true, exitCode, output, string.Empty, false));

		var result = await harness.Manager.IsPackageInstalledAsync(Serial, "com.example.app", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data, Is.EqualTo(expected));
		});
	}

	[TestCase("connected to 192.168.1.20:5555")]
	[TestCase("already connected to 192.168.1.20:5555")]
	public async Task Connecting_over_the_network_returns_the_address_as_serial_and_refreshes_the_devices(string output)
	{
		using var harness = await OnlineHarnessAsync();
		harness.Runner.When(argv => argv.Count == 2 && argv[0] == "connect",
			new AdbProcessResult(true, 0, output, string.Empty, false));
		var listsBefore = harness.Runner.Invocations.Count(IsDevicesList);

		var result = await harness.Manager.ConnectAsync("192.168.1.20:5555", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Data, Is.EqualTo("192.168.1.20:5555"));
			Assert.That(harness.Runner.Invocations, Has.Some.EqualTo(new[] { "connect", "192.168.1.20:5555" }));
			Assert.That(harness.Runner.Invocations.Count(IsDevicesList), Is.GreaterThan(listsBefore));
		});
	}

	[Test]
	public async Task A_device_connected_over_wifi_gets_no_reverse_tunnel_while_a_usb_device_does()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(IsDevicesList,
			new AdbProcessResult(true,
				0,
				"List of devices attached\n" +
				$"{Serial} device usb:1-1 product:shiba model:Pixel_8 transport_id:1\n" +
				"192.168.1.20:5555 device product:tokay model:Pixel_9 transport_id:2\n",
				string.Empty,
				false));

		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var reversed = harness.Runner.Invocations.Where(argv => argv.Contains("reverse")).Select(argv => argv[1]).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(reversed, Does.Contain(Serial));
			Assert.That(reversed, Does.Not.Contain("192.168.1.20:5555"));
			Assert.That(harness.Manager.Devices.Single(device => device.Serial == "192.168.1.20:5555").IsNetworkConnection, Is.True);
		});
	}

	[TestCase("192.168.1.20:5555", true)]
	[TestCase("phone.fritz.box:37215", true)]
	[TestCase("adb-R58M12ABCDE-a1b2c3._adb-tls-connect._tcp", true)]
	[TestCase("R58M12ABCDE", false)]
	[TestCase("emulator-5554", false)]
	[TestCase("127.0.0.1:5555", false)]
	[TestCase("localhost:5555", false)]
	[TestCase("[::1]:5555", false)]
	public void Only_a_device_reached_over_the_network_counts_as_a_network_connection(string serial, bool network)
	{
		var device = new AdbDevice(serial, AdbDeviceState.Device, null, null, null, null, null, DateTimeOffset.UnixEpoch);

		Assert.That(device.IsNetworkConnection, Is.EqualTo(network));
	}

	[TestCase("failed to connect to '192.168.1.20:5555': Connection refused")]
	[TestCase("cannot connect to 192.168.1.20:5555: No route to host (10065)")]
	public async Task A_connect_adb_reports_as_failed_on_exit_zero_is_a_failure(string output)
	{
		using var harness = await OnlineHarnessAsync();
		harness.Runner.When(argv => argv.Count == 2 && argv[0] == "connect",
			new AdbProcessResult(true, 0, output, string.Empty, false));

		var result = await harness.Manager.ConnectAsync("192.168.1.20:5555", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(AdbFailureCode.CommandFailed));
			Assert.That(result.ErrorMessage, Does.Contain("192.168.1.20:5555"));
		});
	}

	[TestCase("192.168.1.20")]
	[TestCase("192.168.1.20:0")]
	[TestCase("192.168.1.20:70000")]
	[TestCase("-L:5555")]
	[TestCase("phone.local:5555 extra")]
	[TestCase("")]
	public async Task An_address_that_is_not_host_and_port_is_refused_before_adb_runs(string address)
	{
		using var harness = await OnlineHarnessAsync();

		var result = await harness.Manager.ConnectAsync(address, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(AdbFailureCode.InvalidParameter));
			Assert.That(harness.Runner.Invocations.Any(argv => argv.Contains("connect")), Is.False);
		});
	}

	[Test]
	public async Task An_uninstall_that_older_adb_reports_as_Failure_on_exit_zero_is_a_failure()
	{
		using var harness = await OnlineHarnessAsync();
		harness.Runner.When(argv => argv.Contains("uninstall"),
			new AdbProcessResult(true, 0, "Failure [DELETE_FAILED_INTERNAL_ERROR]", string.Empty, false));

		var result = await harness.Manager.UninstallPackageAsync(Serial, "com.example.app", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(AdbFailureCode.CommandFailed));
			Assert.That(result.ErrorMessage, Does.Contain("DELETE_FAILED_INTERNAL_ERROR"));
		});
	}

	[Test]
	public async Task Pushing_a_local_file_that_does_not_exist_is_refused_before_adb_runs()
	{
		using var harness = await OnlineHarnessAsync();
		var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");

		var result = await harness.Manager.PushFileAsync(Serial, missing, "/sdcard/x.bin", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(AdbFailureCode.InvalidParameter));
			Assert.That(harness.Runner.Invocations.Any(argv => argv.Contains("push")), Is.False);
		});
	}

	[Test]
	public async Task A_battery_reading_is_shared_while_it_is_fresh_and_read_again_once_it_is_not()
	{
		using var harness = await OnlineHarnessAsync();
		harness.Runner.When(argv => IsShell(argv) && argv[3] == "dumpsys battery",
			new AdbProcessResult(true, 0, "  AC powered: false\n  status: 3\n  health: 2\n  level: 40\n", string.Empty, false));

		await harness.Manager.GetBatteryAsync(Serial, CancellationToken.None);
		await harness.Manager.GetBatteryAsync(Serial, CancellationToken.None);
		var readsWhileFresh = BatteryReads(harness);
		harness.TimeProvider.Advance(TimeSpan.FromSeconds(11));
		var later = await harness.Manager.GetBatteryAsync(Serial, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(readsWhileFresh, Is.EqualTo(1));
			Assert.That(BatteryReads(harness), Is.EqualTo(2));
			Assert.That(later.Data!.Level, Is.EqualTo(40));
		});
	}

	[Test]
	public async Task A_device_that_does_not_report_its_battery_answers_Unsupported()
	{
		using var harness = await OnlineHarnessAsync();
		harness.Runner.When(argv => IsShell(argv) && argv[3] == "dumpsys battery",
			new AdbProcessResult(true, 127, string.Empty, "dumpsys: not found", false));

		var result = await harness.Manager.GetBatteryAsync(Serial, CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(AdbFailureCode.Unsupported));
	}

	[Test]
	public async Task A_battery_read_that_fails_for_a_moment_is_a_command_failure_not_a_missing_feature()
	{
		using var harness = await OnlineHarnessAsync();
		harness.Runner.When(argv => IsShell(argv) && argv[3] == "dumpsys battery",
			new AdbProcessResult(true, 1, string.Empty, "error: device offline", false));

		var result = await harness.Manager.GetBatteryAsync(Serial, CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(AdbFailureCode.CommandFailed));
	}

	[Test]
	public async Task SnapshotChanged_fires_for_a_state_change_but_not_for_a_poll_that_changes_nothing_plugins_see()
	{
		using var harness = new AdbManagerHarness();
		var output = Devices("device");
		harness.Runner.When(IsDevicesList, _ => new AdbProcessResult(true, 0, output, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		var raised = 0;
		harness.Manager.SnapshotChanged += (_, _) => raised++;

		harness.TimeProvider.Advance(TimeSpan.FromSeconds(3));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		var afterIdlePoll = raised;
		output = Devices("offline");
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(afterIdlePoll, Is.Zero);
			Assert.That(raised, Is.EqualTo(1));
		});
	}

	private static async Task<AdbManagerHarness> OnlineHarnessAsync(string state = "device")
	{
		var harness = new AdbManagerHarness();
		harness.Runner.When(IsDevicesList, new AdbProcessResult(true, 0, Devices(state), string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		return harness;
	}

	private static int BatteryReads(AdbManagerHarness harness)
		=> harness.Runner.Invocations.Count(argv => IsShell(argv) && argv[3] == "dumpsys battery");

	private static bool IsDevicesList(IReadOnlyList<string> argv)
		=> argv.Count == 2 && argv[0] == "devices" && argv[1] == "-l";

	private static bool IsShell(IReadOnlyList<string> argv)
		=> argv.Count == 4 && argv[0] == "-s" && argv[2] == "shell";

	private static string Devices(string state)
		=> $"List of devices attached\n{Serial} {state} product:shiba model:Pixel_8 transport_id:1\n";
}
