using MacroDeck.Sdk.Android;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Infrastructure.Adb;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
public class InProcessAndroidDeviceTypedOperationsTests
{
	private static readonly AdbStatus _enabled = AdbStatus.Disabled with { Enabled = true };

	private FakeAdbManager _adb = null!;
	private FakeHostLockState _lock = null!;
	private InProcessAndroidDeviceManager _manager = null!;

	[SetUp]
	public void SetUp()
	{
		_adb = new FakeAdbManager { Status = _enabled, Devices = [Device("device-1")] };
		_lock = new FakeHostLockState();
		_manager = new InProcessAndroidDeviceManager(_adb, new FakeAdbDeviceOperations(), _lock, Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		_manager.Dispose();
	}

	private IAndroidDevice Phone => _manager.FindDevice("device-1")!;

	private static AdbDevice Device(string serial, AdbDeviceState state = AdbDeviceState.Device)
	{
		return new AdbDevice(serial, state, "Pixel_8", "Google", "shiba", "1", null, DateTimeOffset.UtcNow);
	}

	private T SingleCommand<T>()
		where T : AdbCommand
	{
		Assert.That(_adb.ExecutedCommands, Has.Count.EqualTo(1));
		Assert.That(_adb.ExecutedCommands[0], Is.InstanceOf<T>());
		return (T)_adb.ExecutedCommands[0];
	}

	[TestCase(AndroidKey.Home, AdbKey.Home)]
	[TestCase(AndroidKey.Back, AdbKey.Back)]
	[TestCase(AndroidKey.Enter, AdbKey.Enter)]
	[TestCase(AndroidKey.Menu, AdbKey.Menu)]
	[TestCase(AndroidKey.Search, AdbKey.Search)]
	[TestCase(AndroidKey.VolumeUp, AdbKey.VolumeUp)]
	[TestCase(AndroidKey.VolumeDown, AdbKey.VolumeDown)]
	[TestCase(AndroidKey.VolumeMute, AdbKey.VolumeMute)]
	[TestCase(AndroidKey.MediaPlayPause, AdbKey.MediaPlayPause)]
	[TestCase(AndroidKey.MediaNext, AdbKey.MediaNext)]
	[TestCase(AndroidKey.MediaPrevious, AdbKey.MediaPrevious)]
	[TestCase(AndroidKey.Power, AdbKey.Power)]
	[TestCase(AndroidKey.Sleep, AdbKey.Sleep)]
	[TestCase(AndroidKey.Wakeup, AdbKey.Wakeup)]
	[TestCase(AndroidKey.AppSwitch, AdbKey.AppSwitch)]
	[TestCase(AndroidKey.DpadUp, AdbKey.DpadUp)]
	[TestCase(AndroidKey.DpadDown, AdbKey.DpadDown)]
	[TestCase(AndroidKey.DpadLeft, AdbKey.DpadLeft)]
	[TestCase(AndroidKey.DpadRight, AdbKey.DpadRight)]
	[TestCase(AndroidKey.DpadCenter, AdbKey.DpadCenter)]
	public async Task Every_key_runs_the_matching_key_event_on_this_device(AndroidKey key, AdbKey expected)
	{
		await Phone.SendKeyAsync(key);

		Assert.That(SingleCommand<AdbKeyEventCommand>(), Is.EqualTo(new AdbKeyEventCommand("device-1", expected)));
	}

	[Test]
	public async Task The_app_link_text_and_touch_operations_run_their_fixed_commands()
	{
		await Phone.StartAppAsync("com.example.app");
		await Phone.ForceStopAppAsync("com.example.app");
		await Phone.OpenUriAsync("https://example.com");
		await Phone.InputTextAsync("hello");
		await Phone.TapAsync(10, 20);
		await Phone.SwipeAsync(1, 2, 3, 4, TimeSpan.FromMilliseconds(250));

		Assert.That(_adb.ExecutedCommands,
			Is.EqualTo(new AdbCommand[]
			{
				new AdbStartAppCommand("device-1", "com.example.app"),
				new AdbForceStopAppCommand("device-1", "com.example.app"),
				new AdbOpenUriCommand("device-1", "https://example.com"),
				new AdbInputTextCommand("device-1", "hello"),
				new AdbTapCommand("device-1", 10, 20),
				new AdbSwipeCommand("device-1", 1, 2, 3, 4, 250)
			}));
	}

	[TestCase(AndroidRebootMode.Normal, AdbRebootMode.Normal)]
	[TestCase(AndroidRebootMode.Recovery, AdbRebootMode.Recovery)]
	[TestCase(AndroidRebootMode.Bootloader, AdbRebootMode.Bootloader)]
	public async Task Reboot_runs_the_matching_mode(AndroidRebootMode mode, AdbRebootMode expected)
	{
		await Phone.RebootAsync(mode);

		Assert.That(SingleCommand<AdbRebootCommand>(), Is.EqualTo(new AdbRebootCommand("device-1", expected)));
	}

	[TestCase(AdbFailureCode.Disabled, AndroidDeviceErrorCode.AdbNotEnabled)]
	[TestCase(AdbFailureCode.ExecutableNotFound, AndroidDeviceErrorCode.AdbUnavailable)]
	[TestCase(AdbFailureCode.ServerUnreachable, AndroidDeviceErrorCode.AdbUnavailable)]
	[TestCase(AdbFailureCode.DeviceNotFound, AndroidDeviceErrorCode.DeviceNotFound)]
	[TestCase(AdbFailureCode.DeviceUnauthorized, AndroidDeviceErrorCode.DeviceUnauthorized)]
	[TestCase(AdbFailureCode.DeviceOffline, AndroidDeviceErrorCode.DeviceOffline)]
	[TestCase(AdbFailureCode.Timeout, AndroidDeviceErrorCode.Timeout)]
	[TestCase(AdbFailureCode.InvalidParameter, AndroidDeviceErrorCode.InvalidArgument)]
	[TestCase(AdbFailureCode.CommandFailed, AndroidDeviceErrorCode.CommandFailed)]
	[TestCase(AdbFailureCode.Unsupported, AndroidDeviceErrorCode.Unsupported)]
	public void An_adb_failure_throws_the_matching_error_code_and_message(
		AdbFailureCode failure,
		AndroidDeviceErrorCode expected)
	{
		_adb.ExecuteResult = Result.Fail<AdbFailureCode>(failure, "failure detail");

		var exception = Assert.ThrowsAsync<AndroidDeviceException>(() => Phone.SendKeyAsync(AndroidKey.Home));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.ErrorCode, Is.EqualTo(expected));
			Assert.That(exception.Message, Is.EqualTo("failure detail"));
		});
	}

	[Test]
	public async Task A_screenshot_returns_the_png_bytes_of_this_device()
	{
		byte[] png = [137, 80, 78, 71];
		_adb.CaptureScreenshotResult = Result.Ok<byte[], AdbFailureCode>(png);

		var bytes = await Phone.CaptureScreenshotAsync();

		Assert.Multiple(() =>
		{
			Assert.That(bytes, Is.EqualTo(png));
			Assert.That(_adb.CaptureScreenshotSerials, Is.EqualTo(new[] { "device-1" }));
		});
	}

	[Test]
	public async Task The_status_is_the_hosts_latest_reading_and_null_before_the_first()
	{
		var before = await Phone.GetStatusAsync();
		_adb.PropertiesResult = new AdbDeviceProperties(80, true, false, "com.example.app", DateTimeOffset.UtcNow);
		var after = await Phone.GetStatusAsync();

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.Null);
			Assert.That(after,
				Is.EqualTo(new AndroidDeviceStatus
					{ IsScreenOn = true, IsLocked = false, ForegroundPackage = "com.example.app" }));
			Assert.That(_adb.PropertiesSerials, Is.EqualTo(new[] { "device-1", "device-1" }));
		});
	}

	[Test]
	public async Task Typed_operations_run_while_the_host_is_locked_and_the_shell_stays_refused()
	{
		_lock.IsLocked = true;

		await Phone.SendKeyAsync(AndroidKey.Wakeup);
		await Phone.RebootAsync();
		var shell = Assert.ThrowsAsync<AndroidDeviceException>(() => Phone.ExecuteShellAsync("reboot"));

		Assert.Multiple(() =>
		{
			Assert.That(_adb.ExecutedCommands, Has.Count.EqualTo(2));
			Assert.That(shell!.ErrorCode, Is.EqualTo(AndroidDeviceErrorCode.HostLocked));
		});
	}

	[Test]
	public void A_device_without_usb_permission_reads_unauthorized_and_lacking_usb_permission()
	{
		var changes = new List<string>();
		_manager.DeviceStateChanged += (_, e)
			=> changes.Add($"{e.PreviousState}>{e.Device.State}:{e.Device.LacksUsbPermission}");

		_adb.Devices = [Device("device-1", AdbDeviceState.Unauthorized)];
		_adb.RaiseSnapshotChanged();
		_adb.Devices = [Device("device-1", AdbDeviceState.NoPermissions)];
		_adb.RaiseSnapshotChanged();

		Assert.Multiple(() =>
		{
			Assert.That(Phone.State, Is.EqualTo(AndroidDeviceState.Unauthorized));
			Assert.That(Phone.LacksUsbPermission, Is.True);
			Assert.That(changes, Is.EqualTo(new[] { "Online>Unauthorized:False", "Unauthorized>Unauthorized:True" }));
		});
	}

	[TestCase(AdbDeviceState.Bootloader, AndroidDeviceMode.Bootloader)]
	[TestCase(AdbDeviceState.Recovery, AndroidDeviceMode.Recovery)]
	[TestCase(AdbDeviceState.Sideload, AndroidDeviceMode.Sideload)]
	[TestCase(AdbDeviceState.Unknown, AndroidDeviceMode.Unknown)]
	[TestCase(AdbDeviceState.Offline, AndroidDeviceMode.Normal)]
	public void A_device_outside_android_reads_offline_with_its_mode_and_the_change_is_reported(
		AdbDeviceState state,
		AndroidDeviceMode mode)
	{
		var changes = 0;
		_adb.Devices = [Device("device-1", AdbDeviceState.Offline)];
		_adb.RaiseSnapshotChanged();
		_manager.DeviceStateChanged += (_, _) => changes++;

		_adb.Devices = [Device("device-1", state)];
		_adb.RaiseSnapshotChanged();

		Assert.Multiple(() =>
		{
			Assert.That(Phone.State, Is.EqualTo(AndroidDeviceState.Offline));
			Assert.That(Phone.Mode, Is.EqualTo(mode));
			Assert.That(changes, Is.EqualTo(mode == AndroidDeviceMode.Normal ? 0 : 1));
		});
	}

	[Test]
	public void A_missing_adb_executable_is_reported_and_its_arrival_raises_access_changed()
	{
		var raised = 0;
		_manager.AccessChanged += (_, _) => raised++;
		var missing = _manager.IsAdbUnavailable;

		_adb.Status = _enabled with { ResolvedExecutablePath = "/usr/bin/adb" };
		_adb.RaiseSnapshotChanged();

		Assert.Multiple(() =>
		{
			Assert.That(missing, Is.True);
			Assert.That(_manager.IsAdbUnavailable, Is.False);
			Assert.That(raised, Is.EqualTo(1));
		});
	}

	[Test]
	public void With_adb_turned_off_adb_is_not_reported_missing()
	{
		_adb.Status = AdbStatus.Disabled;
		_adb.RaiseSnapshotChanged();

		Assert.That(_manager.IsAdbUnavailable, Is.False);
	}

	[Test]
	public void The_default_device_is_the_configured_one_even_after_it_disconnected()
	{
		_adb.Devices = [Device("device-1"), Device("device-2")];
		_adb.Status = _enabled with { DefaultDeviceSerial = "device-2" };
		_adb.RaiseSnapshotChanged();
		var configured = _manager.DefaultDevice;

		_adb.Devices = [Device("device-1"), Device("device-2", AdbDeviceState.Disconnected)];
		_adb.RaiseSnapshotChanged();

		Assert.Multiple(() =>
		{
			Assert.That(configured?.Serial, Is.EqualTo("device-2"));
			Assert.That(_manager.DefaultDevice, Is.SameAs(configured));
			Assert.That(_manager.FindDevice("device-2"), Is.Null);
		});
	}

	[Test]
	public void Without_a_configured_default_the_only_attached_device_is_the_default()
	{
		var single = _manager.DefaultDevice;
		_adb.Devices = [Device("device-1"), Device("device-2")];
		_adb.RaiseSnapshotChanged();

		Assert.Multiple(() =>
		{
			Assert.That(single?.Serial, Is.EqualTo("device-1"));
			Assert.That(_manager.DefaultDevice, Is.Null);
		});
	}

	[Test]
	public void With_adb_turned_off_there_is_no_default_device()
	{
		_adb.Status = AdbStatus.Disabled;
		_adb.RaiseSnapshotChanged();

		Assert.That(_manager.DefaultDevice, Is.Null);
	}
}
