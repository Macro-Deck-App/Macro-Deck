using System.Text;
using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
internal sealed class DeviceTileFirstFitClaimTests
{
	private const string TileId = "battery-tile";

	private const string FirstFitTree = """
		{"id":"group","type":"ui.first-fit","properties":{},"children":[
			{"id":"group.inline","type":"ui.text","properties":{"text":"Mouse"}},
			{"id":"group.stacked","type":"ui.stack","children":[
				{"id":"group.stacked.refresh","type":"ui.button","properties":{"events":["press"]}}]}]}
		""";

	private static readonly string[] _control = ["group.stacked.refresh"];

	private DeviceSurfaceFixture _fixture = null!;
	private Guid _deviceId;

	[SetUp]
	public async Task SetUp()
	{
		_fixture = new DeviceSurfaceFixture();
		var tile = DeviceSurfaceFixture.Button(TileId, 0, 0);
		tile.Type = "com.example.battery.tile";
		_fixture.Home.Widgets.Add(tile);
		_deviceId = await _fixture.OpenDeviceAsync();
		_fixture.UiBroker.Tree = new UiRawJson(Encoding.UTF8.GetBytes(
			$$"""{"revision":3,"surface":{"kind":"widget"},"root":{{FirstFitTree}}}"""));
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	[Test]
	public async Task A_press_goes_to_a_control_in_any_layout_because_the_host_cannot_know_which_one_fits()
	{
		var target = new DeviceInteractionTarget { WidgetId = TileId };
		var press = _fixture.Service.SubmitInteractionAsync(_deviceId,
			new DeviceInteraction { Kind = DeviceInteractionKind.Press, Target = target });
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(100));
		await DeviceSurfaceFixture.DrainAsync();
		await press;
		await _fixture.Service.SubmitInteractionAsync(_deviceId,
			new DeviceInteraction { Kind = DeviceInteractionKind.Release, Target = target });
		await DeviceSurfaceFixture.DrainAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Triggers.TriggerTypes, Is.Empty);
			Assert.That(_fixture.UiBroker.HostEvents.Select(sent => sent.Command.NodeId).Distinct(), Is.EqualTo(_control));
		});
	}
}
