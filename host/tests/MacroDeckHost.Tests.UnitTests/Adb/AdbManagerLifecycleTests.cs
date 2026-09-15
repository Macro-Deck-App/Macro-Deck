using System.Diagnostics;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Infrastructure.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Adb;

public class AdbManagerLifecycleTests
{
	private const string Serial = "R58M12ABCDE";

	[Test]
	public async Task ShutdownAsync_completes_within_its_cap_even_when_every_adb_call_hangs()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var marker = new AdbOwnershipMarker(harness.Paths, new LoggerConfiguration().CreateLogger());
		marker.Write(new AdbOwnershipState(1, false, [new AdbOwnedTunnel(Serial, 8193, 8193)], DateTimeOffset.UtcNow));

		harness.Runner.HangForever = new TaskCompletionSource<AdbProcessResult>();
		var stopwatch = Stopwatch.StartNew();

		await harness.Manager.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
		stopwatch.Stop();

		Assert.That(stopwatch.Elapsed,
			Is.LessThan(TimeSpan.FromSeconds(5)),
			"ShutdownAsync must enforce its own cap rather than waiting out a hung adb call");
	}

	[Test]
	public async Task ShutdownAsync_removes_every_owned_tunnel_even_when_one_device_fails()
	{
		using var harness = new AdbManagerHarness();
		const string other = "OTHERSERIAL";
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var marker = new AdbOwnershipMarker(harness.Paths, new LoggerConfiguration().CreateLogger());
		marker.Write(new AdbOwnershipState(1,
			false,
			[new AdbOwnedTunnel(Serial, 8193, 8193), new AdbOwnedTunnel(other, 8194, 8193)],
			DateTimeOffset.UtcNow));
		harness.Runner.When(argv => argv.Contains("--remove") && argv.Contains(Serial),
			_ => throw new IOException("device disconnected mid-call"));

		await harness.Manager.ShutdownAsync();

		var removeCalls = harness.Runner.Invocations.Where(argv => argv.Contains("--remove")).ToList();
		Assert.Multiple(() =>
		{
			Assert.That(removeCalls.Any(argv => argv.Contains(Serial)),
				Is.True,
				"the failing device's removal must still be attempted");
			Assert.That(removeCalls.Any(argv => argv.Contains(other)),
				Is.True,
				"one device's failure must not skip the others");
		});
	}

	[Test]
	public async Task ShutdownAsync_deletes_the_ownership_marker_after_a_clean_pass()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var marker = new AdbOwnershipMarker(harness.Paths, new LoggerConfiguration().CreateLogger());
		marker.Write(new AdbOwnershipState(1, false, [new AdbOwnedTunnel(Serial, 8193, 8193)], DateTimeOffset.UtcNow));
		Assert.That(marker.Read(),
			Is.Not.Null,
			"the marker must exist before shutdown for this test to prove anything");

		await harness.Manager.ShutdownAsync();

		Assert.That(marker.Read(), Is.Null);
	}

	[Test]
	public async Task ShutdownAsync_never_invokes_kill_server()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		await harness.Manager.ShutdownAsync();

		Assert.That(harness.Runner.Invocations.Any(argv => argv.Contains("kill-server")), Is.False);
	}

	[Test]
	public async Task Disabling_adb_never_invokes_kill_server()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		harness.PreferenceService.AdbSettings = harness.PreferenceService.AdbSettings with { Enabled = false };
		await harness.Manager.ApplySettingsAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Runner.Invocations.Any(argv => argv.Contains("kill-server")), Is.False);
			Assert.That(harness.Manager.Status.Enabled, Is.False);
		});
	}

	[TestCase(false, true, TestName = "Turning off ADB removes the tunnel Macro Deck created")]
	[TestCase(true, false, TestName = "Turning off USB connections removes the tunnel Macro Deck created")]
	public async Task Switching_off_removes_only_the_recorded_tunnel(bool enabled, bool usbConnectionsEnabled)
	{
		using var harness = ConnectedDevice();
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		var devicePort = AdbUsbTunnelPorts.DeviceSideCandidates[0];
		Assert.That(harness.Manager.Devices.Single().Tunnel?.Established, Is.True, "precondition: tunnel created");

		harness.PreferenceService.AdbSettings = harness.PreferenceService.AdbSettings with
		{
			Enabled = enabled, UsbConnectionsEnabled = usbConnectionsEnabled
		};
		await harness.Manager.ApplySettingsAsync(CancellationToken.None);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var removeCalls = harness.Runner.Invocations.Where(argv => argv.Contains("--remove")).ToList();
		Assert.That(removeCalls,
			Is.EqualTo(new[] { new[] { "-s", Serial, "reverse", "--remove", "tcp:" + devicePort } }),
			"exactly the recorded mapping is removed, once, and nothing else on the device is touched");
	}

	[Test]
	public async Task Turning_usb_connections_back_on_recreates_the_tunnel()
	{
		using var harness = ConnectedDevice();
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		harness.PreferenceService.AdbSettings = harness.PreferenceService.AdbSettings with { UsbConnectionsEnabled = false };
		await harness.Manager.ApplySettingsAsync(CancellationToken.None);
		harness.Runner.Invocations.Clear();

		harness.PreferenceService.AdbSettings = harness.PreferenceService.AdbSettings with { UsbConnectionsEnabled = true };
		await harness.Manager.ApplySettingsAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(harness.Runner.Invocations.Any(argv => argv.Contains("--no-rebind")), Is.True);
			Assert.That(harness.Manager.Devices.Single().Tunnel?.Established, Is.True);
		});
	}

	[Test]
	public async Task Tunnels_left_by_an_unclean_run_are_still_removed_after_starting_with_adb_disabled()
	{
		var paths = new TestPaths();
		new AdbOwnershipMarker(paths, new LoggerConfiguration().CreateLogger())
			.Write(new AdbOwnershipState(1, false, [new AdbOwnedTunnel(Serial, 8194, 8193)], DateTimeOffset.UtcNow));
		using var harness = ConnectedDevice(paths);
		harness.PreferenceService.AdbSettings = harness.PreferenceService.AdbSettings with { Enabled = false };

		await harness.Manager.ApplySettingsAsync(CancellationToken.None);
		Assert.That(harness.Runner.Invocations.Any(argv => argv.Contains("--remove")), Is.False);

		harness.PreferenceService.AdbSettings = harness.PreferenceService.AdbSettings with { Enabled = true };
		await harness.Manager.ApplySettingsAsync(CancellationToken.None);

		Assert.That(harness.Runner.Invocations,
			Has.Some.EqualTo(new[] { "-s", Serial, "reverse", "--remove", "tcp:8194" }));
	}

	private static AdbManagerHarness ConnectedDevice(TestPaths? paths = null, string devicesStandardError = "")
	{
		var harness = new AdbManagerHarness(paths: paths);
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, "UsbFfs tcp:8195 tcp:9999\n", string.Empty, false));
		harness.Runner.When(argv => argv.Count == 2 && argv[0] == "devices",
			new AdbProcessResult(true,
				0,
				$"List of devices attached\n{Serial} device product:p model:Pixel transport_id:1\n",
				devicesStandardError,
				false));
		return harness;
	}

	[Test]
	public async Task RestartServerAsync_is_the_only_path_that_invokes_kill_server_while_stop_on_exit_is_off()
	{
		using var harness = new AdbManagerHarness();
		harness.Runner.When(argv => argv.Contains("--list"),
			new AdbProcessResult(true, 0, string.Empty, string.Empty, false));
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		var result = await harness.Manager.RestartServerAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(harness.Runner.Invocations.Any(argv => argv.Contains("kill-server")), Is.True);
		});
	}

	private const string DaemonStartedOutput =
		"* daemon not running; starting now at tcp:5037\n* daemon started successfully\n";

	private static readonly AdbProcessResult Ok = new(true, 0, string.Empty, string.Empty, false);
	private static readonly AdbProcessResult DaemonStarted = new(true, 0, string.Empty, DaemonStartedOutput, false);
	private static readonly AdbProcessResult Unreachable = new(true, 1, string.Empty, "cannot connect to daemon", false);

	[Test]
	public async Task Exit_stops_a_server_Macro_Deck_started_after_removing_its_tunnels()
	{
		using var harness = ConnectedDevice(devicesStandardError: DaemonStartedOutput);
		EnableStopOnExit(harness);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		await harness.Manager.ShutdownAsync();

		var invocations = harness.Runner.Invocations;
		var killAt = invocations.FindIndex(argv => argv.Contains("kill-server"));
		var lastRemoveAt = invocations.FindLastIndex(argv => argv.Contains("--remove"));
		Assert.Multiple(() =>
		{
			Assert.That(lastRemoveAt, Is.GreaterThanOrEqualTo(0), "precondition: the tunnel is removed at exit");
			Assert.That(killAt, Is.GreaterThan(lastRemoveAt), "the server is stopped only after its tunnels are gone");
		});
	}

	[Test]
	public async Task Exit_leaves_a_server_that_was_already_running()
	{
		using var harness = StopOnExitHarness(() => Ok);

		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		await harness.Manager.ShutdownAsync();

		Assert.That(KilledServer(harness), Is.False);
	}

	[Test]
	public async Task Exit_leaves_the_server_Macro_Deck_started_while_stop_on_exit_is_off()
	{
		using var harness = StopOnExitHarness(() => DaemonStarted, stopServerOnExit: false);

		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		await harness.Manager.ShutdownAsync();

		Assert.That(KilledServer(harness), Is.False);
	}

	[TestCase(true, TestName = "Exit stops a server the start-server fallback launched")]
	[TestCase(false, TestName = "Exit leaves a server the start-server fallback only found running")]
	public async Task Start_server_fallback_claims_the_server_only_when_it_launched_it(bool launched)
	{
		using var harness = StopOnExitHarness(() => Unreachable);
		harness.Runner.When(argv => argv[0] == "start-server", launched ? DaemonStarted : Ok);

		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		await harness.Manager.ShutdownAsync();

		Assert.That(KilledServer(harness), Is.EqualTo(launched));
	}

	[Test]
	public async Task Exit_leaves_a_server_found_after_adb_was_disabled_and_enabled_again()
	{
		var devices = DaemonStarted;
		using var harness = StopOnExitHarness(() => devices);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		harness.PreferenceService.AdbSettings = harness.PreferenceService.AdbSettings with { Enabled = false };
		await harness.Manager.ApplySettingsAsync(CancellationToken.None);
		devices = Ok;
		harness.PreferenceService.AdbSettings = harness.PreferenceService.AdbSettings with { Enabled = true };
		await harness.Manager.ApplySettingsAsync(CancellationToken.None);
		await harness.Manager.ShutdownAsync();

		Assert.That(KilledServer(harness), Is.False);
	}

	[Test]
	public async Task Exit_leaves_a_server_that_came_back_after_Macro_Decks_own_went_away()
	{
		var devices = DaemonStarted;
		using var harness = StopOnExitHarness(() => devices);
		harness.Runner.When(argv => argv[0] == "start-server", Unreachable);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);

		devices = Unreachable;
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		devices = Ok;
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		await harness.Manager.ShutdownAsync();

		Assert.That(KilledServer(harness), Is.False);
	}

	[Test]
	public async Task Exit_stops_the_server_a_restart_from_Macro_Deck_launched()
	{
		using var harness = StopOnExitHarness(() => Ok);
		harness.Runner.When(argv => argv[0] == "start-server", DaemonStarted);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		await harness.Manager.RestartServerAsync(CancellationToken.None);
		harness.Runner.Invocations.Clear();

		await harness.Manager.ShutdownAsync();

		Assert.That(KilledServer(harness), Is.True);
	}

	[Test]
	public async Task Exit_with_stop_on_exit_still_completes_within_its_cap_when_every_adb_call_hangs()
	{
		using var harness = StopOnExitHarness(() => DaemonStarted);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		harness.Runner.HangForever = new TaskCompletionSource<AdbProcessResult>();
		var stopwatch = Stopwatch.StartNew();

		await harness.Manager.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(5)));
	}

	[Test]
	public async Task Exit_skips_stopping_the_server_while_a_restart_is_still_in_flight()
	{
		using var harness = StopOnExitHarness(() => DaemonStarted);
		await harness.Manager.RefreshNowAsync(CancellationToken.None);
		using var restartEntered = new ManualResetEventSlim();
		using var releaseRestart = new ManualResetEventSlim();
		harness.Runner.When(argv => argv[0] == "start-server",
			_ =>
			{
				restartEntered.Set();
				releaseRestart.Wait(TimeSpan.FromSeconds(10));
				return DaemonStarted;
			});
		var restart = Task.Run(() => harness.Manager.RestartServerAsync(CancellationToken.None));
		Assert.That(restartEntered.Wait(TimeSpan.FromSeconds(5)), Is.True, "precondition: the restart holds the gate");

		await harness.Manager.ShutdownAsync().WaitAsync(TimeSpan.FromSeconds(5));
		var killCalls = harness.Runner.Invocations.Count(argv => argv.Contains("kill-server"));
		releaseRestart.Set();
		await restart;

		Assert.That(killCalls, Is.EqualTo(1), "only the restart's own kill-server ran; exit gave up on the held gate");
	}

	private static AdbManagerHarness StopOnExitHarness(Func<AdbProcessResult> devices, bool stopServerOnExit = true)
	{
		var harness = new AdbManagerHarness();
		EnableStopOnExit(harness, stopServerOnExit);
		harness.Runner.When(argv => argv.Contains("--list"), Ok);
		harness.Runner.When(argv => argv.Count == 2 && argv[0] == "devices", _ => devices());
		return harness;
	}

	private static void EnableStopOnExit(AdbManagerHarness harness, bool stopServerOnExit = true)
		=> harness.PreferenceService.AdbSettings =
			harness.PreferenceService.AdbSettings with { StopServerOnExit = stopServerOnExit };

	private static bool KilledServer(AdbManagerHarness harness)
		=> harness.Runner.Invocations.Any(argv => argv.Contains("kill-server"));
}
