using MacroDeck.Sdk.Android;
using MacroDeckHost.Integrations.Adb.Connections;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
internal sealed class AdbDeviceTrackerTests
{
	private ScriptedAndroidDevices _devices = null!;
	private ManualTimeProvider _time = null!;
	private AdbDeviceTracker _tracker = null!;
	private List<string> _events = null!;

	[SetUp]
	public void SetUp()
	{
		_devices = new ScriptedAndroidDevices();
		_time = new ManualTimeProvider();
		_events = [];
	}

	[TearDown]
	public void TearDown()
	{
		_tracker?.Dispose();
	}

	private void Track()
	{
		_tracker = new AdbDeviceTracker(_devices, _time);
		_tracker.Changed += (_, change) => _events.Add($"{change.Kind}:{change.Device.Serial}:{change.Device.Status}");
	}

	[TestCase(null, AdbDeviceStatus.Device, AdbDeviceChangeKind.Connected)]
	[TestCase(null, AdbDeviceStatus.Unauthorized, AdbDeviceChangeKind.Connected)]
	[TestCase(null, AdbDeviceStatus.NoPermissions, AdbDeviceChangeKind.Connected)]
	[TestCase(AdbDeviceStatus.Disconnected, AdbDeviceStatus.Device, AdbDeviceChangeKind.Online)]
	[TestCase(AdbDeviceStatus.Disconnected, AdbDeviceStatus.Unauthorized, AdbDeviceChangeKind.Online)]
	[TestCase(AdbDeviceStatus.Disconnected, AdbDeviceStatus.Offline, AdbDeviceChangeKind.Online)]
	[TestCase(AdbDeviceStatus.Disconnected, AdbDeviceStatus.NoPermissions, AdbDeviceChangeKind.Online)]
	[TestCase(AdbDeviceStatus.Device, AdbDeviceStatus.Disconnected, AdbDeviceChangeKind.Disconnected)]
	[TestCase(AdbDeviceStatus.NoPermissions, AdbDeviceStatus.Disconnected, AdbDeviceChangeKind.Disconnected)]
	[TestCase(AdbDeviceStatus.Unauthorized, AdbDeviceStatus.Device, AdbDeviceChangeKind.Authorized)]
	[TestCase(AdbDeviceStatus.Authorizing, AdbDeviceStatus.Device, AdbDeviceChangeKind.Authorized)]
	[TestCase(AdbDeviceStatus.Offline, AdbDeviceStatus.Device, AdbDeviceChangeKind.Authorized)]
	[TestCase(AdbDeviceStatus.Device, AdbDeviceStatus.Unauthorized, AdbDeviceChangeKind.Unauthorized)]
	[TestCase(AdbDeviceStatus.NoPermissions, AdbDeviceStatus.Unauthorized, AdbDeviceChangeKind.Unauthorized)]
	[TestCase(AdbDeviceStatus.Device, AdbDeviceStatus.Offline, AdbDeviceChangeKind.Offline)]
	[TestCase(AdbDeviceStatus.Unauthorized, AdbDeviceStatus.Offline, AdbDeviceChangeKind.Offline)]
	[TestCase(AdbDeviceStatus.Device, AdbDeviceStatus.NoPermissions, null)]
	[TestCase(AdbDeviceStatus.Unauthorized, AdbDeviceStatus.NoPermissions, null)]
	[TestCase(AdbDeviceStatus.Device, AdbDeviceStatus.Authorizing, null)]
	[TestCase(AdbDeviceStatus.Device, AdbDeviceStatus.Recovery, null)]
	[TestCase(AdbDeviceStatus.Device, AdbDeviceStatus.Bootloader, null)]
	[TestCase(AdbDeviceStatus.Device, AdbDeviceStatus.Sideload, null)]
	[TestCase(AdbDeviceStatus.Device, AdbDeviceStatus.Unknown, null)]
	[TestCase(AdbDeviceStatus.Offline, AdbDeviceStatus.Bootloader, null)]
	[TestCase(AdbDeviceStatus.Recovery, AdbDeviceStatus.Offline, AdbDeviceChangeKind.Offline)]
	[TestCase(AdbDeviceStatus.Recovery, AdbDeviceStatus.Device, AdbDeviceChangeKind.Authorized)]
	[TestCase(AdbDeviceStatus.Disconnected, AdbDeviceStatus.Recovery, AdbDeviceChangeKind.Online)]
	[TestCase(null, AdbDeviceStatus.Sideload, AdbDeviceChangeKind.Connected)]
	[TestCase(AdbDeviceStatus.Device, AdbDeviceStatus.Device, null)]
	[TestCase(AdbDeviceStatus.Disconnected, AdbDeviceStatus.Disconnected, null)]
	public void Each_transition_raises_the_event_it_always_raised(
		AdbDeviceStatus? previous,
		AdbDeviceStatus current,
		AdbDeviceChangeKind? expected)
	{
		Assert.That(AdbDeviceTracker.KindFor(previous, current), Is.EqualTo(expected));
	}

	[Test]
	public void Devices_attached_before_tracking_starts_raise_nothing()
	{
		_devices.Attach("S1", null);

		Track();

		Assert.Multiple(() =>
		{
			Assert.That(_events, Is.Empty);
			Assert.That(_tracker.Devices.Single().Status, Is.EqualTo(AdbDeviceStatus.Device));
		});
	}

	[Test]
	public void A_device_that_comes_back_within_five_minutes_is_online_again_not_connected()
	{
		Track();

		_devices.Attach("S1", null);
		_devices.Detach("S1");
		_time.Advance(TimeSpan.FromMinutes(4));
		_devices.Attach("S1", null);

		Assert.That(_events,
			Is.EqualTo(new[] { "Connected:S1:Device", "Disconnected:S1:Disconnected", "Online:S1:Device" }));
	}

	[Test]
	public void A_device_that_comes_back_after_five_minutes_is_connected_again()
	{
		Track();

		_devices.Attach("S1", null);
		_devices.Detach("S1");
		_time.Advance(TimeSpan.FromMinutes(6));
		_devices.Attach("S1", null);

		Assert.That(_events,
			Is.EqualTo(new[] { "Connected:S1:Device", "Disconnected:S1:Disconnected", "Connected:S1:Device" }));
	}

	[Test]
	public void A_disconnected_device_keeps_its_model_for_the_disconnected_event()
	{
		Track();
		var changes = new List<AdbDeviceChange>();
		_tracker.Changed += (_, change) => changes.Add(change);

		_devices.Attach("S1", null, model: "Pixel 8");
		_devices.Detach("S1");

		Assert.That(changes.Last().Device.Model, Is.EqualTo("Pixel 8"));
	}

	[Test]
	public void Losing_usb_permission_raises_nothing_and_getting_it_back_while_unauthorized_raises_unauthorized()
	{
		Track();
		_devices.Attach("S1", null);

		_devices.SetState("S1", AndroidDeviceState.Unauthorized, true);
		_devices.SetState("S1", AndroidDeviceState.Unauthorized);

		Assert.That(_events,
			Is.EqualTo(new[] { "Connected:S1:Device", "Unauthorized:S1:Unauthorized" }));
	}

	[Test]
	public void A_device_lacking_usb_permission_is_reported_as_such_when_it_connects()
	{
		Track();

		_devices.Attach("S1", null, state: AndroidDeviceState.Unauthorized, lacksUsbPermission: true);

		Assert.That(_events, Is.EqualTo(new[] { "Connected:S1:NoPermissions" }));
	}

	[Test]
	public void Connecting_then_authorizing_raises_connected_then_authorized()
	{
		Track();

		_devices.Attach("S1", null, state: AndroidDeviceState.Connecting);
		_devices.SetState("S1", AndroidDeviceState.Online);
		_devices.SetState("S1", AndroidDeviceState.Offline);

		Assert.That(_events,
			Is.EqualTo(new[] { "Connected:S1:Authorizing", "Authorized:S1:Device", "Offline:S1:Offline" }));
	}

	[Test]
	public void A_device_going_into_recovery_raises_nothing_and_leaving_it_for_offline_raises_offline()
	{
		Track();
		_devices.Attach("S1", null);

		_devices.SetState("S1", AndroidDeviceState.Offline, mode: AndroidDeviceMode.Recovery);
		_devices.SetState("S1", AndroidDeviceState.Offline);

		Assert.That(_events, Is.EqualTo(new[] { "Connected:S1:Device", "Offline:S1:Offline" }));
	}

	[Test]
	public void A_device_that_connects_in_the_bootloader_is_reported_with_that_state()
	{
		Track();

		_devices.Attach("S1", null, state: AndroidDeviceState.Offline, mode: AndroidDeviceMode.Bootloader);

		Assert.That(_events, Is.EqualTo(new[] { "Connected:S1:Bootloader" }));
	}

	[Test]
	public void Withdrawn_access_disconnects_every_device()
	{
		Track();
		_devices.Attach("S1", null);

		_devices.SetAccess(AndroidDeviceAccess.AdbNotEnabled);

		Assert.That(_events, Is.EqualTo(new[] { "Connected:S1:Device", "Disconnected:S1:Disconnected" }));
	}

	[Test]
	public void After_dispose_nothing_is_raised()
	{
		Track();
		_tracker.Dispose();

		_devices.Attach("S1", null);

		Assert.That(_events, Is.Empty);
	}
}
