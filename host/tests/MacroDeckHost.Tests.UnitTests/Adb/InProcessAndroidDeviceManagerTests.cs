using MacroDeck.Sdk.Android;
using MacroDeckHost.Application.Adb;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Infrastructure.Adb;
using MacroDeckHost.Tests.UnitTests.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
public class InProcessAndroidDeviceManagerTests
{
	private static readonly AdbStatus _enabled = AdbStatus.Disabled with { Enabled = true };

	private FakeAdbManager _adb = null!;
	private FakeAdbDeviceOperations _operations = null!;
	private FakeHostLockState _lock = null!;
	private InProcessAndroidDeviceManager _manager = null!;
	private List<string> _events = null!;

	[SetUp]
	public void SetUp()
	{
		_adb = new FakeAdbManager
			{ Status = _enabled, Devices = [Device("A1", AdbDeviceState.Device, "0123456789abcdef")] };
		_operations = new FakeAdbDeviceOperations();
		_lock = new FakeHostLockState();
		_manager = new InProcessAndroidDeviceManager(_adb, _operations, _lock, Logger.None);
		_events = [];
		_manager.AccessChanged += (_, _) => _events.Add("access");
		_manager.DeviceConnected += (_, e) => _events.Add($"connected:{e.Device.Serial}");
		_manager.DeviceDisconnected += (_, e) => _events.Add($"disconnected:{e.Device.Serial}");
		_manager.DeviceStateChanged += (_, e)
			=> _events.Add($"state:{e.Device.Serial}:{e.PreviousState}>{e.Device.State}");
	}

	[TearDown]
	public void TearDown()
	{
		_manager.Dispose();
	}

	private static AdbDevice Device(string serial, AdbDeviceState state, string? stableId = null)
	{
		return new AdbDevice(serial, state, "Pixel_8", "Google", "shiba", "1", null, DateTimeOffset.UtcNow, stableId);
	}

	[Test]
	public void With_adb_enabled_a_built_in_integration_sees_the_attached_devices_without_any_plugin_consent()
	{
		var device = _manager.Devices.Single();

		Assert.Multiple(() =>
		{
			Assert.That(_manager.Access, Is.EqualTo(AndroidDeviceAccess.Available));
			Assert.That(device.Serial, Is.EqualTo("A1"));
			Assert.That(device.State, Is.EqualTo(AndroidDeviceState.Online));
			Assert.That(device.Info,
				Is.EqualTo(new AndroidDeviceInfo("Pixel_8", "Google", "shiba") { StableId = "0123456789abcdef" }));
		});
	}

	[Test]
	public void A_changed_adb_snapshot_raises_connected_state_changed_and_disconnected()
	{
		var original = _manager.Devices.Single();
		_adb.Devices =
		[
			Device("A1", AdbDeviceState.Unauthorized),
			Device("B2", AdbDeviceState.Authorizing),
			Device("GONE", AdbDeviceState.Disconnected)
		];
		_adb.RaiseSnapshotChanged();
		_adb.Devices = [Device("B2", AdbDeviceState.Authorizing)];
		_adb.RaiseSnapshotChanged();

		Assert.Multiple(() =>
		{
			Assert.That(_events,
				Is.EqualTo(new[] { "state:A1:Online>Unauthorized", "connected:B2", "disconnected:A1" }));
			Assert.That(_manager.Devices.Select(device => device.Serial), Is.EqualTo(new[] { "B2" }));
			Assert.That(_manager.FindDevice("GONE"), Is.Null);
			Assert.That(original.State, Is.EqualTo(AndroidDeviceState.Unauthorized), "the instance stays valid");
		});
	}

	[Test]
	public void Turning_adb_off_withdraws_access_and_disconnects_every_device()
	{
		_adb.Status = AdbStatus.Disabled;
		_adb.RaiseSnapshotChanged();

		Assert.Multiple(() =>
		{
			Assert.That(_manager.Access, Is.EqualTo(AndroidDeviceAccess.AdbNotEnabled));
			Assert.That(_manager.Devices, Is.Empty);
			Assert.That(_events, Is.EqualTo(new[] { "access", "disconnected:A1" }));
		});
	}

	[Test]
	public async Task A_locked_host_refuses_the_shell_and_connect_but_still_reads_the_battery()
	{
		_lock.IsLocked = true;
		var device = _manager.Devices.Single();

		var shell = Assert.ThrowsAsync<AndroidDeviceException>(()
			=> device.ExecuteShellAsync("getprop ro.build.characteristics"));
		var connect = Assert.ThrowsAsync<AndroidDeviceException>(() => _manager.ConnectAsync("192.168.1.20:5555"));
		var battery = await device.GetBatteryStateAsync();

		Assert.Multiple(() =>
		{
			Assert.That(shell!.ErrorCode, Is.EqualTo(AndroidDeviceErrorCode.HostLocked));
			Assert.That(connect!.ErrorCode, Is.EqualTo(AndroidDeviceErrorCode.HostLocked));
			Assert.That(_operations.ConnectedAddresses, Is.Empty);
			Assert.That(battery,
				Is.EqualTo(new AndroidBatteryState
				{
					Level = 50,
					IsCharging = false,
					Status = AndroidBatteryStatus.Discharging,
					Health = AndroidBatteryHealth.Good
				}));
		});
	}

	[Test]
	public async Task Shell_output_is_cut_to_the_same_budget_plugins_get()
	{
		_operations.Shell = _ => Task.FromResult(Result.Ok<AdbShellOutput, AdbFailureCode>(new AdbShellOutput(0,
			new string('x', AdbDeviceCallPolicy.ShellResultBudgetBytes * 2),
			string.Empty,
			false)));

		var result = await _manager.Devices.Single().ExecuteShellAsync("logcat -d");

		Assert.Multiple(() =>
		{
			Assert.That(result.Truncated, Is.True);
			Assert.That(result.StandardOutput.Length, Is.LessThan(AdbDeviceCallPolicy.ShellResultBudgetBytes));
		});
	}

	[Test]
	public void A_failed_adb_call_throws_the_matching_error_code()
	{
		_operations.Battery = Result.Fail<AdbBatteryReading, AdbFailureCode>(AdbFailureCode.DeviceOffline, "offline");

		var failure = Assert.ThrowsAsync<AndroidDeviceException>(()
			=> _manager.Devices.Single().GetBatteryStateAsync());

		Assert.That(failure!.ErrorCode, Is.EqualTo(AndroidDeviceErrorCode.DeviceOffline));
	}
}
