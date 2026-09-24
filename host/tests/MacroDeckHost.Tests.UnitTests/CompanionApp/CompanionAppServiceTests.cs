using System.Collections.Concurrent;
using System.Security.Cryptography;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Application.CompanionApp;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Ui.Transport.Messages.CompanionApp;
using MacroDeckHost.CompanionApp;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.Companion;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.CompanionApp;

public class CompanionAppServiceTests
{
	private const string Serial = "R58M12ABCDE";

	[Test]
	public async Task A_device_without_the_app_can_get_it_and_a_fresh_install_is_not_launched()
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = null;
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.That(Fixture.Only(await fixture.StatusAsync()).State, Is.EqualTo(CompanionAppDeviceStates.NotInstalled));

		var (error, status) = await fixture.Service.InstallAsync(Serial, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(error, Is.Null);
			Assert.That(fixture.Operations.Installed, Has.Count.EqualTo(1));
			Assert.That(Fixture.Only(status).State, Is.EqualTo(CompanionAppDeviceStates.UpToDate));
			Assert.That(Fixture.Only(status).InstalledVersion, Is.EqualTo("26.1.0"));
			Assert.That(fixture.Adb.ExecutedCommands.OfType<AdbStartAppCommand>(), Is.Empty);
		});
	}

	[Test]
	public async Task An_older_sideloaded_app_has_an_update_and_a_newer_one_is_up_to_date()
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = 8;
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);
		var outdated = Fixture.Only(await fixture.StatusAsync());

		fixture.Device.InstalledVersionCode = 10;
		await fixture.Service.CheckNowAsync(CancellationToken.None);
		var newer = Fixture.Only(await fixture.StatusAsync());

		Assert.Multiple(() =>
		{
			Assert.That(outdated.State, Is.EqualTo(CompanionAppDeviceStates.UpdateAvailable));
			Assert.That(newer.State, Is.EqualTo(CompanionAppDeviceStates.UpToDate));
		});
	}

	[Test]
	public async Task A_manual_update_relaunches_the_app_even_when_it_was_not_running()
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = 8;
		fixture.Device.Running = "stopped";
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		await fixture.Service.InstallAsync(Serial, CancellationToken.None);

		Assert.That(fixture.Adb.ExecutedCommands.OfType<AdbStartAppCommand>().Single().Package,
			Is.EqualTo(CompanionAppService.PackageName));
	}

	[TestCase("running", true)]
	[TestCase("unknown", true)]
	[TestCase("stopped", false)]
	public async Task Auto_update_installs_an_outdated_sideloaded_app_and_relaunches_it_unless_it_was_stopped(
		string running,
		bool relaunched)
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = 8;
		fixture.Device.Running = running;
		await fixture.Service.SetAutoUpdateAsync(true, CancellationToken.None);

		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Operations.Installed, Has.Count.EqualTo(1));
			Assert.That(fixture.Adb.ExecutedCommands.OfType<AdbStartAppCommand>().Any(), Is.EqualTo(relaunched));
			Assert.That(fixture.Preferences.Values[CompanionAppService.AutoUpdateKey], Is.EqualTo("true"));
		});
	}

	[Test]
	public async Task Auto_update_is_off_until_turned_on()
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = 8;

		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.That(fixture.Operations.Installed, Is.Empty);
		Assert.That((await fixture.StatusAsync()).AutoUpdate, Is.False);
	}

	[Test]
	public async Task A_google_play_install_is_never_touched_by_auto_update()
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = 8;
		fixture.Device.Installer = "com.android.vending";
		await fixture.Service.SetAutoUpdateAsync(true, CancellationToken.None);

		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.That(fixture.Operations.Installed, Is.Empty);
		Assert.That(Fixture.Only(await fixture.StatusAsync()).State,
			Is.EqualTo(CompanionAppDeviceStates.InstalledFromPlayStore));
	}

	[Test]
	public async Task A_failed_auto_update_is_not_retried_for_the_same_version_until_the_device_reconnects()
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = 8;
		fixture.Operations.Result = Result.Fail<AdbFailureCode>(AdbFailureCode.CommandFailed,
			"Performing Streamed Install\nadb: failed to install: Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: signatures do not match]");
		await fixture.Service.SetAutoUpdateAsync(true, CancellationToken.None);

		await fixture.Service.RunDueWorkAsync(CancellationToken.None);
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);
		var failed = Fixture.Only(await fixture.StatusAsync());
		fixture.Adb.Devices = [];
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);
		fixture.Adb.Devices = [Fixture.ReadyDevice()];
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(failed.Error, Is.EqualTo(CompanionAppErrors.IncompatibleSignature));
			Assert.That(failed.State, Is.EqualTo(CompanionAppDeviceStates.UpdateAvailable));
			Assert.That(fixture.Operations.Installed, Has.Count.EqualTo(2));
		});
	}

	[TestCase("adb: failed to install: Failure [INSTALL_FAILED_USER_RESTRICTED: Install canceled by user]",
		CompanionAppErrors.InstallBlockedOnDevice)]
	[TestCase("Failure [INSTALL_FAILED_INSUFFICIENT_STORAGE]", CompanionAppErrors.StorageFull)]
	[TestCase("Failure [INSTALL_FAILED_OLDER_SDK: Requires newer sdk version #23]", CompanionAppErrors.DeviceTooOld)]
	[TestCase("error: closed", CompanionAppErrors.InstallFailed)]
	public async Task An_install_failure_is_reported_as_a_code_not_as_adb_output(string output, string expected)
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = null;
		fixture.Operations.Result = Result.Fail<AdbFailureCode>(AdbFailureCode.CommandFailed, output);
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		var (error, status) = await fixture.Service.InstallAsync(Serial, CancellationToken.None);

		Assert.That(error, Is.EqualTo(expected));
		Assert.That(Fixture.Only(status).Error, Is.EqualTo(expected));
	}

	[Test]
	public async Task A_download_that_fails_verification_is_never_installed()
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = null;
		fixture.Releases.Outcome = CompanionApkDownloadOutcome.VerificationFailed;
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		var (error, _) = await fixture.Service.InstallAsync(Serial, CancellationToken.None);

		Assert.That(error, Is.EqualTo(CompanionAppErrors.VerificationFailed));
		Assert.That(fixture.Operations.Installed, Is.Empty);
	}

	[Test]
	public async Task Installing_needs_adb()
	{
		using var fixture = new Fixture();
		fixture.Adb.Status = AdbStatus.Disabled;

		var (error, status) = await fixture.Service.InstallAsync(Serial, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(error, Is.EqualTo(CompanionAppErrors.AdbDisabled));
			Assert.That(status.AdbEnabled, Is.False);
			Assert.That(status.Devices, Is.Empty);
		});
	}

	[Test]
	public async Task A_linux_adb_appliance_is_not_offered_the_app()
	{
		using var fixture = new Fixture();
		fixture.Device.SdkLevel = null;
		await fixture.Service.SetAutoUpdateAsync(true, CancellationToken.None);

		await fixture.Service.RunDueWorkAsync(CancellationToken.None);
		var (error, status) = await fixture.Service.InstallAsync(Serial, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Fixture.Only(status).State, Is.EqualTo(CompanionAppDeviceStates.NotAndroid));
			Assert.That(error, Is.EqualTo(CompanionAppErrors.NotAndroid));
			Assert.That(fixture.Operations.Installed, Is.Empty);
		});
	}

	[TestCase("23\r\n", CompanionAppDeviceStates.NotInstalled)]
	[TestCase("22\r\n", CompanionAppDeviceStates.DeviceTooOld)]
	public async Task The_android_version_decides_whether_the_app_can_be_installed(string sdk, string expected)
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = null;
		fixture.Device.SdkOutput = sdk;
		fixture.Device.PackageOutput = "Dexopt state:\n  [com.android.settings]\n";

		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.That(Fixture.Only(await fixture.StatusAsync()).State, Is.EqualTo(expected));
	}

	[Test]
	public async Task Connected_android_apps_behind_the_latest_release_are_flagged_and_other_platforms_are_not_listed()
	{
		using var fixture = new Fixture();
		var old = fixture.Harness.AddDevice("Kitchen tablet");
		var current = fixture.Harness.AddDevice("Desk phone");
		var iphone = fixture.Harness.AddDevice("iPhone");
		await fixture.Harness.ReportAsync("c1", old, CompanionHarness.Report("26.0.3"));
		await fixture.Harness.ReportAsync("c2", current, CompanionHarness.Report("26.1.0"));
		var ios = CompanionHarness.Report("26.0.0");
		ios.Platform = "iOS 18.2";
		await fixture.Harness.ReportAsync("c3", iphone, ios);
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		var apps = (await fixture.StatusAsync()).ConnectedApps;

		Assert.That(apps.Select(app => (app.Name, app.AppVersion, app.UpdateAvailable)),
			Is.EqualTo(new[] { ("Desk phone", "26.1.0", false), ("Kitchen tablet", "26.0.3", true) }));
	}

	[Test]
	public async Task A_battery_report_from_a_connected_app_does_not_announce_a_change()
	{
		using var fixture = new Fixture();
		var device = fixture.Harness.AddDevice("Kitchen tablet");
		await fixture.Harness.ReportAsync("c1", device, CompanionHarness.Report("26.0.3"));
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);
		var announced = fixture.Announcements;

		var report = CompanionHarness.Report("26.0.3");
		report.BatteryLevelPercent = 42;
		fixture.Harness.DeviceRegistry.Report("c1", device, report);
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.That(fixture.Announcements, Is.EqualTo(announced));
	}

	[Test]
	public async Task Nothing_is_fetched_while_neither_adb_nor_a_companion_app_is_in_use()
	{
		using var fixture = new Fixture();
		fixture.Adb.Status = AdbStatus.Disabled;

		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.That(fixture.Releases.ManifestReads, Is.Zero);
	}

	[Test]
	public async Task The_next_wake_up_stays_in_the_future_after_adb_is_turned_off()
	{
		using var fixture = new Fixture();
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);
		fixture.Adb.Status = AdbStatus.Disabled;
		fixture.Adb.Devices = [];
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		fixture.Time.Advance(TimeSpan.FromHours(7));
		var next = await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.That(next is null || next > fixture.Time.Now, Is.True, $"next {next} at {fixture.Time.Now}");
		Assert.That(fixture.Releases.ManifestReads, Is.EqualTo(1));
	}

	[Test]
	public async Task A_failing_check_is_retried_later_rather_than_at_once()
	{
		using var fixture = new Fixture();
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);
		fixture.Releases.Reachable = false;
		fixture.Time.Advance(CompanionAppService.RefreshInterval);

		var first = await fixture.Service.RunDueWorkAsync(CancellationToken.None);
		var second = await fixture.Service.RunDueWorkAsync(CancellationToken.None);
		var status = await fixture.StatusAsync();

		Assert.Multiple(() =>
		{
			Assert.That(fixture.Releases.ManifestReads, Is.EqualTo(2));
			Assert.That(first, Is.GreaterThan(fixture.Time.Now));
			Assert.That(second, Is.GreaterThan(fixture.Time.Now));
			Assert.That(status.CheckFailed, Is.True);
			Assert.That(status.LatestVersion, Is.EqualTo("26.1.0"));
		});
	}

	[Test]
	public async Task A_device_that_stays_connected_is_read_again_so_an_uninstall_shows_up()
	{
		using var fixture = new Fixture();
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);
		fixture.Device.InstalledVersionCode = null;

		fixture.Time.Advance(CompanionAppService.DeviceRecheckInterval);
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.That(Fixture.Only(await fixture.StatusAsync()).State, Is.EqualTo(CompanionAppDeviceStates.NotInstalled));
	}

	[Test]
	public async Task The_host_refuses_to_install_over_a_google_play_install()
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = 8;
		fixture.Device.Installer = "com.android.vending";

		var (error, _) = await fixture.Service.InstallAsync(Serial, CancellationToken.None);

		Assert.That(error, Is.EqualTo(CompanionAppErrors.PlayStoreInstall));
		Assert.That(fixture.Operations.Installed, Is.Empty);
	}

	[Test]
	public async Task A_device_that_cannot_be_read_is_not_installed_on_so_a_google_play_copy_is_never_overwritten()
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = 8;
		fixture.Device.Installer = "com.android.vending";
		fixture.Device.PackageFailure = AdbFailureCode.Timeout;

		var (error, status) = await fixture.Service.InstallAsync(Serial, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(error, Is.EqualTo(CompanionAppErrors.DeviceNotReady));
			Assert.That(Fixture.Only(status).State, Is.EqualTo(CompanionAppDeviceStates.Unknown));
			Assert.That(fixture.Operations.Installed, Is.Empty);
		});
	}

	[Test]
	public async Task A_device_whose_android_version_cannot_be_read_is_not_installed_on()
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = null;
		fixture.Device.SdkFailure = AdbFailureCode.Timeout;

		var (error, _) = await fixture.Service.InstallAsync(Serial, CancellationToken.None);

		Assert.That(error, Is.EqualTo(CompanionAppErrors.DeviceNotReady));
		Assert.That(fixture.Operations.Installed, Is.Empty);
	}

	[Test]
	public async Task An_up_to_date_device_is_left_alone()
	{
		using var fixture = new Fixture();

		var (error, _) = await fixture.Service.InstallAsync(Serial, CancellationToken.None);

		Assert.That(error, Is.Null);
		Assert.That(fixture.Operations.Installed, Is.Empty);
	}

	[Test]
	public async Task A_failure_message_goes_away_once_the_app_on_the_device_changes()
	{
		using var fixture = new Fixture();
		fixture.Device.InstalledVersionCode = 8;
		fixture.Operations.Result = Result.Fail<AdbFailureCode>(AdbFailureCode.CommandFailed,
			"Failure [INSTALL_FAILED_UPDATE_INCOMPATIBLE: signatures do not match]");
		await fixture.Service.InstallAsync(Serial, CancellationToken.None);

		fixture.Device.InstalledVersionCode = null;
		fixture.Time.Advance(CompanionAppService.DeviceRecheckInterval);
		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		var device = Fixture.Only(await fixture.StatusAsync());
		Assert.That(device.State, Is.EqualTo(CompanionAppDeviceStates.NotInstalled));
		Assert.That(device.Error, Is.Null);
	}

	[Test]
	public async Task A_device_waiting_for_usb_debugging_approval_is_listed_as_such()
	{
		using var fixture = new Fixture();
		fixture.Adb.Devices = [Fixture.ReadyDevice() with { State = AdbDeviceState.Unauthorized }];

		await fixture.Service.RunDueWorkAsync(CancellationToken.None);

		Assert.That(Fixture.Only(await fixture.StatusAsync()).State, Is.EqualTo(CompanionAppDeviceStates.NotAuthorized));
	}

	private sealed class Fixture : IDisposable
	{
		public Fixture()
		{
			Adb.Status = AdbStatus.Disabled with { Enabled = true, ResolvedExecutablePath = "adb" };
			Adb.Devices = [ReadyDevice()];
			Adb.QueryResultFor = Device.Answer;
			Operations.Device = Device;
			var services = new ServiceCollection();
			services.AddSingleton<IAppPreferenceRepository>(Preferences);
			services.AddSingleton<IDeviceRepository>(Harness.Devices);
			Service = new CompanionAppService(Adb,
				Operations,
				Releases,
				Harness.DeviceRegistry,
				services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
				Harness.Transport,
				Paths,
				Time,
				new LoggerConfiguration().WriteTo.Sink(new NullSink()).CreateLogger());
		}

		public CompanionHarness Harness { get; } = new();
		public FakeAdbManager Adb { get; } = new();
		public ScriptedDevice Device { get; } = new();
		public RecordingOperations Operations { get; } = new();
		public ScriptedReleases Releases { get; } = new();
		public MemoryPreferences Preferences { get; } = new();
		public TestPaths Paths { get; } = new();
		public Delegation.FakeTimeProvider Time { get; } = new();
		public CompanionAppService Service { get; }

		public int Announcements
			=> Harness.Transport.GroupMessages.Count(message => message.Message is CompanionAppChangedEvent);

		public static AdbDevice ReadyDevice()
			=> new(Serial, AdbDeviceState.Device, "Pixel_8", "Google", null, "1", null, DateTimeOffset.UnixEpoch);

		public Task<CompanionAppStatus> StatusAsync() => Service.GetStatusAsync(CancellationToken.None);

		public static CompanionAppDevice Only(CompanionAppStatus status) => status.Devices.Single();

		public void Dispose()
		{
			Service.Dispose();
			if (Directory.Exists(Paths.BaseDirectory))
			{
				Directory.Delete(Paths.BaseDirectory, recursive: true);
			}
		}
	}

	internal sealed class ScriptedDevice
	{
		public int? SdkLevel { get; set; } = 34;
		public string? SdkOutput { get; set; }
		public int? InstalledVersionCode { get; set; } = 9;
		public string Installer { get; set; } = "null";
		public string? PackageOutput { get; set; }
		public string Running { get; set; } = "running";
		public AdbFailureCode? SdkFailure { get; set; }
		public AdbFailureCode? PackageFailure { get; set; }

		public Result<string, AdbFailureCode> Answer(AdbQueryCommand command)
			=> command switch
			{
				AdbSdkLevelCommand when SdkFailure is { } failure => Result.Fail<string, AdbFailureCode>(failure, "timed out"),
				AdbPackageInfoCommand when PackageFailure is { } failure => Result.Fail<string, AdbFailureCode>(failure, "timed out"),
				AdbSdkLevelCommand when SdkOutput is { } output => Ok(output),
				AdbSdkLevelCommand when SdkLevel is { } level => Ok($"{level}\n"),
				AdbSdkLevelCommand => Result.Fail<string, AdbFailureCode>(AdbFailureCode.CommandFailed, "getprop: not found"),
				AdbPackageInfoCommand when PackageOutput is { } output => Ok(output),
				AdbPackageInfoCommand when InstalledVersionCode is { } code =>
					Ok(CompanionPackageInfoParserTests.Installed(code, code == 9 ? "26.1.0" : $"26.0.{code}", Installer)),
				AdbPackageInfoCommand => Ok("Unable to find package: app.macrodeck.companion\n"),
				AdbPackageRunningCommand => Ok(Running + "\n"),
				_ => Ok(string.Empty)
			};

		private static Result<string, AdbFailureCode> Ok(string output) => Result.Ok<string, AdbFailureCode>(output);
	}

	internal sealed class RecordingOperations : IAdbDeviceOperations
	{
		public ScriptedDevice Device { get; set; } = null!;
		public List<(string Serial, string Path)> Installed { get; } = [];
		public Result<AdbFailureCode> Result { get; set; } = Domain.Common.Result.Ok<AdbFailureCode>();

		public Task<Result<AdbFailureCode>> InstallApkAsync(string serial, string apkPath, CancellationToken cancellationToken)
		{
			Installed.Add((serial, apkPath));
			if (Result.Success)
			{
				Device.InstalledVersionCode = 9;
				Device.PackageOutput = null;
			}

			return Task.FromResult(Result);
		}

		public Task<Result<AdbShellOutput, AdbFailureCode>> RunShellAsync(string serial, string command, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<Result<AdbBatteryReading, AdbFailureCode>> GetBatteryAsync(string serial, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<Result<AdbFailureCode>> PushFileAsync(string serial, string localPath, string remotePath, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<Result<AdbFailureCode>> PullFileAsync(string serial, string remotePath, string localPath, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<Result<AdbFailureCode>> UninstallPackageAsync(string serial, string packageName, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<Result<bool, AdbFailureCode>> IsPackageInstalledAsync(string serial, string packageName, CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public Task<Result<string, AdbFailureCode>> ConnectAsync(string address, CancellationToken cancellationToken)
			=> throw new NotSupportedException();
	}

	internal sealed class ScriptedReleases : ICompanionAppReleaseClient
	{
		private static readonly byte[] Apk = [1, 2, 3, 4];

		public CompanionApkDownloadOutcome Outcome { get; set; } = CompanionApkDownloadOutcome.Downloaded;
		public int ManifestReads { get; private set; }
		public bool Reachable { get; set; } = true;

		public Task<CompanionAppRelease?> GetLatestAsync(CancellationToken cancellationToken)
		{
			ManifestReads++;
			if (!Reachable)
			{
				return Task.FromResult<CompanionAppRelease?>(null);
			}

			return Task.FromResult<CompanionAppRelease?>(new CompanionAppRelease("26.1.0",
				9,
				new Uri("https://packages.macro-deck.app/companion/android/26.1.0/macro-deck-companion-26.1.0.apk"),
				Convert.ToHexStringLower(SHA256.HashData(Apk)),
				null));
		}

		public async Task<CompanionApkDownloadOutcome> DownloadAsync(CompanionAppRelease release,
			string destinationPath,
			CancellationToken cancellationToken)
		{
			if (Outcome == CompanionApkDownloadOutcome.Downloaded)
			{
				await File.WriteAllBytesAsync(destinationPath, Apk, cancellationToken);
			}

			return Outcome;
		}
	}

	internal sealed class MemoryPreferences : IAppPreferenceRepository
	{
		public ConcurrentDictionary<string, string> Values { get; } = new();

		public Task<AppPreferenceEntity?> GetByKey(string key)
			=> Task.FromResult(Values.TryGetValue(key, out var value)
				? new AppPreferenceEntity { Key = key, Value = value }
				: null);

		public Task SetValue(string key, string value)
		{
			Values[key] = value;
			return Task.CompletedTask;
		}
	}

	private sealed class NullSink : ILogEventSink
	{
		public void Emit(LogEvent logEvent)
		{
		}
	}
}
