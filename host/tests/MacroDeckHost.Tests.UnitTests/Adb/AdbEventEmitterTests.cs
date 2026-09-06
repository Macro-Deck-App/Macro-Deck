using MacroDeckHost.Integrations.Adb;

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

	private static AdbGatewayDevice Device(
		string serial = "SERIAL1",
		AdbGatewayDeviceState state = AdbGatewayDeviceState.Device,
		string? model = "Pixel 8",
		string? manufacturer = "Google")
		=> new(serial, state, model, manufacturer, TunnelEstablished: true);

	[TestCase(AdbGatewayDeviceChangeKind.Connected, AdbEventIds.DeviceConnected)]
	[TestCase(AdbGatewayDeviceChangeKind.Disconnected, AdbEventIds.DeviceDisconnected)]
	[TestCase(AdbGatewayDeviceChangeKind.Authorized, AdbEventIds.DeviceAuthorized)]
	[TestCase(AdbGatewayDeviceChangeKind.Unauthorized, AdbEventIds.DeviceUnauthorized)]
	[TestCase(AdbGatewayDeviceChangeKind.Online, AdbEventIds.DeviceOnline)]
	[TestCase(AdbGatewayDeviceChangeKind.Offline, AdbEventIds.DeviceOffline)]
	public void Each_kind_publishes_exactly_one_occurrence_with_the_right_id(
		AdbGatewayDeviceChangeKind kind,
		string expectedEventId)
	{
		_emitter.Publish(new AdbGatewayDeviceChange(kind, Device()));

		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published, Has.Count.EqualTo(1));
			Assert.That(_publisher.Published[0].EventId, Is.EqualTo(expectedEventId));
		});
	}

	[Test]
	public void DeviceConnected_carries_the_manufacturer_and_state()
	{
		_emitter.Publish(new AdbGatewayDeviceChange(AdbGatewayDeviceChangeKind.Connected,
			Device(serial: "S1", state: AdbGatewayDeviceState.Unauthorized, model: "Pixel 8", manufacturer: "Google")));

		var parameters = _publisher.Published[0].Parameters!;
		Assert.Multiple(() =>
		{
			Assert.That(parameters["serial"], Is.EqualTo("S1"));
			Assert.That(parameters["model"], Is.EqualTo("Pixel 8"));
			Assert.That(parameters["manufacturer"], Is.EqualTo("Google"));
			Assert.That(parameters["state"], Is.EqualTo("Unauthorized"));
		});
	}

	[TestCase(AdbGatewayDeviceChangeKind.Disconnected)]
	[TestCase(AdbGatewayDeviceChangeKind.Authorized)]
	[TestCase(AdbGatewayDeviceChangeKind.Unauthorized)]
	[TestCase(AdbGatewayDeviceChangeKind.Online)]
	[TestCase(AdbGatewayDeviceChangeKind.Offline)]
	public void Every_other_kind_carries_only_serial_and_model(AdbGatewayDeviceChangeKind kind)
	{
		_emitter.Publish(new AdbGatewayDeviceChange(kind, Device(serial: "S1", model: "Pixel 8")));

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

		foreach (var kind in Enum.GetValues<AdbGatewayDeviceChangeKind>())
		{
			_publisher.Published.Clear();
			_emitter.Publish(new AdbGatewayDeviceChange(kind, Device()));
			Assert.That(declaredIds, Does.Contain(_publisher.Published[0].EventId), kind.ToString());
		}
	}
}
