using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Devices;
using HostMapper = MacroDeckHost.Application.Plugins.Capabilities.Mapping.DeviceSurfaceMapper;
using PluginMapper = MacroDeck.Plugin.Hosting.Capabilities.DeviceProvider.DeviceSurfaceMapper;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
public class DeviceSurfaceLabelsContractTests
{
	[Test]
	public void A_buttons_label_position_and_extra_labels_reach_a_plugin_as_the_host_built_them()
	{
		var appearance = new DeviceSurfaceAppearance
		{
			Label = "Main",
			LabelX = 10,
			LabelY = 80,
			ExtraLabels =
			[
				new DeviceSurfaceLabel
				{
					Text = "Top",
					Color = "#ff0000",
					FontSize = 22.5,
					X = 3,
					Y = 96
				},
				new DeviceSurfaceLabel { Text = "Low", Y = 100 }
			]
		};
		var surface = new DeviceSurface
		{
			Revision = 1,
			Profile = new DeviceSurfaceProfile("p", "P"),
			Folder = new DeviceSurfaceFolder("f", "F", null, true),
			Layout = new DeviceSurfaceLayout { Rows = 2, Columns = 3 },
			Widgets =
			[
				new DeviceSurfaceWidget
				{
					Id = "w1",
					Type = "ActionButton",
					PositionX = 0,
					PositionY = 0,
					Width = 1,
					Height = 1,
					Appearance = appearance
				}
			]
		};

		var wire = JsonSerializer.Serialize(HostMapper.ToDto(surface), PluginProtocolJson.Options);
		var received = PluginMapper
			.ToSurface(JsonSerializer.Deserialize<DeviceSurfaceDto>(wire, PluginProtocolJson.Options)!)
			.Widgets.Single()
			.Appearance!;

		Assert.Multiple(() =>
		{
			Assert.That(received.Label, Is.EqualTo("Main"));
			Assert.That(received.LabelX, Is.EqualTo(10));
			Assert.That(received.LabelY, Is.EqualTo(80));
			Assert.That(received.ExtraLabels, Is.EqualTo(appearance.ExtraLabels));
		});
	}
}
