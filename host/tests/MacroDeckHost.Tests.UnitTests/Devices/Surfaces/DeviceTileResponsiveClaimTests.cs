using System.Text;
using System.Text.Json;
using MacroDeck.Sdk.Devices;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Devices.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;

namespace MacroDeckHost.Tests.UnitTests.Devices.Surfaces;

[TestFixture]
internal sealed class DeviceTileResponsiveClaimTests
{
	private const string SmallTileId = "small-tile";
	private const string WideTileId = "wide-tile";

	private const string ResponsiveTree = """
		{"id":"weather","type":"ui.responsive","properties":{"variants":[{"minWidth":1.5}]},"children":[
			{"id":"weather.compact","type":"ui.text","properties":{"text":"21"}},
			{"id":"weather.wide","type":"ui.stack","children":[
				{"id":"weather.wide.refresh","type":"ui.button","properties":{"events":["press"]}}]}]}
		""";

	private static readonly string[] _shortPressTriggers = ["onTouchStart", "onTouchEnd", "onShortPress"];
	private static readonly string[] _wideControl = ["weather.wide.refresh"];

	private DeviceSurfaceFixture _fixture = null!;
	private Guid _deviceId;

	[SetUp]
	public async Task SetUp()
	{
		_fixture = new DeviceSurfaceFixture();
		var small = DeviceSurfaceFixture.Button(SmallTileId, 0, 0);
		small.Type = "com.example.weather.tile";
		var wide = DeviceSurfaceFixture.Button(WideTileId, 0, 1, width: 2);
		wide.Type = "com.example.weather.tile";
		_fixture.Home.Widgets.AddRange([small, wide]);
		_deviceId = await _fixture.OpenDeviceAsync();
		_fixture.UiBroker.Tree = new UiRawJson(Encoding.UTF8.GetBytes(
			$$"""{"revision":3,"surface":{"kind":"widget"},"root":{{ResponsiveTree}}}"""));
	}

	[TearDown]
	public void TearDown() => _fixture.Dispose();

	private async Task PressAsync(string widgetId)
	{
		var target = new DeviceInteractionTarget { WidgetId = widgetId };
		var press = _fixture.Service.SubmitInteractionAsync(_deviceId,
			new DeviceInteraction { Kind = DeviceInteractionKind.Press, Target = target });
		_fixture.Time.Advance(TimeSpan.FromMilliseconds(100));
		await DeviceSurfaceFixture.DrainAsync();
		await press;
		await _fixture.Service.SubmitInteractionAsync(_deviceId,
			new DeviceInteraction { Kind = DeviceInteractionKind.Release, Target = target });
		await DeviceSurfaceFixture.DrainAsync();
	}

	[Test]
	public async Task A_one_cell_tile_leaves_the_press_to_its_flow_when_only_its_wide_layout_has_a_control()
	{
		await PressAsync(SmallTileId);

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.UiBroker.HostEvents, Is.Empty);
			Assert.That(_fixture.Triggers.TriggerTypes, Is.EqualTo(_shortPressTriggers));
		});
	}

	[Test]
	public async Task A_two_cell_tile_sends_the_press_to_the_control_its_wide_layout_shows()
	{
		await PressAsync(WideTileId);

		Assert.Multiple(() =>
		{
			Assert.That(_fixture.Triggers.TriggerTypes, Is.Empty);
			Assert.That(_fixture.UiBroker.HostEvents.Select(sent => sent.Command.NodeId).Distinct(),
				Is.EqualTo(_wideControl));
		});
	}

	[Test]
	public void The_host_claims_the_node_the_shared_fixture_names_for_every_tile_box()
	{
		var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
		while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "ui-model", "fixtures")))
		{
			directory = directory.Parent;
		}

		var fixtures = Path.Combine(directory!.FullName, "ui-model", "fixtures", "component-profile");
		using var tree = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "conformance-responsive-tree.json")));
		using var layout = JsonDocument.Parse(File.ReadAllText(Path.Combine(fixtures, "conformance-responsive-layout.json")));

		Assert.Multiple(() =>
		{
			foreach (var @case in layout.RootElement.GetProperty("cases").EnumerateArray())
			{
				var tile = @case.GetProperty("tile");
				if (@case.TryGetProperty("span", out var span))
				{
					var spacing = span.GetProperty("spacing").GetInt32();
					Assert.That(
						(DeviceInteractionRouter.SpanInCells(span.GetProperty("width").GetInt32(), spacing),
							DeviceInteractionRouter.SpanInCells(span.GetProperty("height").GetInt32(), spacing)),
						Is.EqualTo((Cells(tile, "width"), Cells(tile, "height"))),
						$"{@case.GetProperty("name").GetString()}: the host's box for the span");
				}

				var claim = UiActivationClaim.Of(tree.RootElement, Cells(tile, "width"), Cells(tile, "height"));
				var claimed = @case.GetProperty("claimed");

				Assert.That(claim.Claimant?.GetProperty("id").GetString(),
					Is.EqualTo(claimed.ValueKind == JsonValueKind.String ? claimed.GetString() : null),
					@case.GetProperty("name").GetString());
			}
		});
	}

	private static double? Cells(JsonElement tile, string side)
		=> tile.ValueKind == JsonValueKind.Object && tile.GetProperty(side).ValueKind == JsonValueKind.Number
			? tile.GetProperty(side).GetDouble() / UiLength.Cell
			: null;
}
