using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Callbacks;

[TestFixture]
public class WidgetAppearanceWireTests
{
	private static readonly int[] _backgroundColorOnly = [0];

	[Test]
	public void A_patch_from_a_plugin_that_predates_the_icon_color_leaves_it_unchanged()
	{
		var args = JsonSerializer.Deserialize<WidgetsApplyArgumentsV2>(
			"""{"widgetId":"w1","patch":{"backgroundColor":"#123456"},"stateIds":["current"],"clearProperties":[0]}""",
			PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(args.Patch.BackgroundColor, Is.EqualTo("#123456"));
			Assert.That(args.Patch.IconColor, Is.Null);
			Assert.That(args.ClearProperties, Is.EqualTo(_backgroundColorOnly));
		});
	}

	[Test]
	public void The_icon_color_travels_under_its_frozen_wire_name()
	{
		var json = JsonSerializer.SerializeToElement(new WidgetAppearancePatchDto { IconColor = "#ef4444" },
			PluginProtocolJson.Options);

		var roundTripped = json.Deserialize<WidgetAppearancePatchDto>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(json.GetProperty("iconColor").GetString(), Is.EqualTo("#ef4444"));
			Assert.That(roundTripped.IconColor, Is.EqualTo("#ef4444"));
		});
	}
}
