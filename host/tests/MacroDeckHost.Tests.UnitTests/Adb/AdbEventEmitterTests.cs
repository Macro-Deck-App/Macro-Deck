using MacroDeckHost.Integrations.Adb.Connections;
using MacroDeckHost.Integrations.Adb.Events;

namespace MacroDeckHost.Tests.UnitTests.Adb;

[TestFixture]
internal sealed class AdbEventEmitterTests
{
	private RecordingPublisher _publisher = null!;
	private AdbEventEmitter _emitter = null!;

	[SetUp]
	public void SetUp()
	{
		_publisher = new RecordingPublisher();
		_emitter = new AdbEventEmitter(_publisher);
	}

	private static AdbTrackedDevice Device(
		string serial = "SERIAL1",
		AdbDeviceStatus state = AdbDeviceStatus.Device,
		string? model = "Pixel 8",
		string? manufacturer = "Google")
	{
		return new AdbTrackedDevice(new ScriptedAndroidDevice(serial, null), state, model, manufacturer);
	}

	[TestCase(AdbDeviceChangeKind.Connected, AdbEventIds.DeviceConnected)]
	[TestCase(AdbDeviceChangeKind.Disconnected, AdbEventIds.DeviceDisconnected)]
	[TestCase(AdbDeviceChangeKind.Authorized, AdbEventIds.DeviceAuthorized)]
	[TestCase(AdbDeviceChangeKind.Unauthorized, AdbEventIds.DeviceUnauthorized)]
	[TestCase(AdbDeviceChangeKind.Online, AdbEventIds.DeviceOnline)]
	[TestCase(AdbDeviceChangeKind.Offline, AdbEventIds.DeviceOffline)]
	public void Each_kind_publishes_exactly_one_occurrence_with_the_right_id(
		AdbDeviceChangeKind kind,
		string expectedEventId)
	{
		_emitter.Publish(new AdbDeviceChange(kind, Device()));

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published, Has.Count.EqualTo(1));
			Assert.That(_publisher.Published[0].EventId, Is.EqualTo(expectedEventId));
		});
	}

	[Test]
	public void DeviceConnected_carries_the_manufacturer_and_state()
	{
		_emitter.Publish(new AdbDeviceChange(AdbDeviceChangeKind.Connected,
			Device("S1", AdbDeviceStatus.Unauthorized, "Pixel 8", "Google")));

		var parameters = _publisher.Published[0].Parameters!;
		Assert.Multiple(() =>
		{
			Assert.That(parameters["serial"], Is.EqualTo("S1"));
			Assert.That(parameters["model"], Is.EqualTo("Pixel 8"));
			Assert.That(parameters["manufacturer"], Is.EqualTo("Google"));
			Assert.That(parameters["state"], Is.EqualTo("Unauthorized"));
		});
	}

	[TestCase(AdbDeviceChangeKind.Disconnected)]
	[TestCase(AdbDeviceChangeKind.Authorized)]
	[TestCase(AdbDeviceChangeKind.Unauthorized)]
	[TestCase(AdbDeviceChangeKind.Online)]
	[TestCase(AdbDeviceChangeKind.Offline)]
	public void Every_other_kind_carries_only_serial_and_model(AdbDeviceChangeKind kind)
	{
		_emitter.Publish(new AdbDeviceChange(kind, Device("S1", model: "Pixel 8")));

		var parameters = _publisher.Published[0].Parameters!;
		Assert.Multiple(() =>
		{
			Assert.That(parameters["serial"], Is.EqualTo("S1"));
			Assert.That(parameters["model"], Is.EqualTo("Pixel 8"));
			Assert.That(parameters.ContainsKey("manufacturer"), Is.False);
			Assert.That(parameters.ContainsKey("state"), Is.False);
		});
	}

	[Test]
	public void Every_event_id_used_by_the_emitter_is_declared()
	{
		var declaredIds = AdbEventDefinitions.All.Select(definition => definition.Id).ToList();

		foreach (var kind in Enum.GetValues<AdbDeviceChangeKind>())
		{
			_publisher.Published.Clear();
			_emitter.Publish(new AdbDeviceChange(kind, Device()));
			Assert.That(declaredIds, Does.Contain(_publisher.Published[0].EventId), kind.ToString());
		}
	}
}
