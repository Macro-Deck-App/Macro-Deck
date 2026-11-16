using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.DeviceProvider;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

[TestFixture]
public class DeviceSurfaceLabelsWireCompatibilityTests
{
	[Test]
	public void A_provider_that_predates_extra_labels_still_reads_the_primary_label_of_a_button_that_has_them()
	{
		var appearance = new DeviceSurfaceAppearanceDto
		{
			Label = "Main",
			LabelColor = "#ffffff",
			FontSize = 14,
			TextAlign = "center",
			LabelPosition = "bottom",
			LabelX = 10,
			LabelY = 95,
			ExtraLabels = [new DeviceSurfaceLabelDto { Text = "Second", X = 3, Y = 0 }]
		};

		var asOldProvider = JsonSerializer.Deserialize<AppearanceBeforeExtraLabels>(
			JsonSerializer.Serialize(appearance, PluginProtocolJson.Options),
			PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(asOldProvider.Label, Is.EqualTo("Main"));
			Assert.That(asOldProvider.LabelColor, Is.EqualTo("#ffffff"));
			Assert.That(asOldProvider.FontSize, Is.EqualTo(14));
			Assert.That(asOldProvider.TextAlign, Is.EqualTo("center"));
			Assert.That(asOldProvider.LabelPosition, Is.EqualTo("bottom"));
		});
	}

	[Test]
	public void An_appearance_from_a_host_that_predates_extra_labels_has_none_and_no_offset()
	{
		const string json = """{"label":"Main","labelColor":"#ffffff","textAlign":"center","labelPosition":"bottom"}""";

		var dto = JsonSerializer.Deserialize<DeviceSurfaceAppearanceDto>(json, PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(dto.Label, Is.EqualTo("Main"));
			Assert.That(dto.ExtraLabels, Is.Empty);
			Assert.That(dto.LabelX, Is.Null);
			Assert.That(dto.LabelY, Is.Null);
		});
	}

	[Test]
	public void Extra_labels_and_offsets_round_trip()
	{
		var appearance = new DeviceSurfaceAppearanceDto
		{
			Label = "Main",
			LabelX = 10,
			ExtraLabels =
			[
				new DeviceSurfaceLabelDto
				{
					Text = "Second",
					Color = "#ff0000",
					FontSize = 22.5,
					X = 3,
					Y = 96
				}
			]
		};

		var actual = JsonSerializer.Deserialize<DeviceSurfaceAppearanceDto>(
			JsonSerializer.Serialize(appearance, PluginProtocolJson.Options),
			PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(actual.LabelX, Is.EqualTo(10));
			Assert.That(actual.ExtraLabels.Single(), Is.EqualTo(appearance.ExtraLabels.Single()));
		});
	}

	[Test]
	public void A_patch_from_a_plugin_that_predates_label_numbers_names_no_label()
	{
		const string json = """{"label":"Live","labelColor":"#ff0000"}""";

		var dto = JsonSerializer.Deserialize<WidgetAppearancePatchDto>(json, PluginProtocolJson.Options)!;

		Assert.That(dto.LabelNumber, Is.Null);
	}

	[Test]
	public void A_label_number_round_trips()
	{
		var actual = JsonSerializer.Deserialize<WidgetAppearancePatchDto>(JsonSerializer.Serialize(
				new WidgetAppearancePatchDto { Label = "Live", LabelNumber = 3 },
				PluginProtocolJson.Options),
			PluginProtocolJson.Options)!;

		Assert.That(actual.LabelNumber, Is.EqualTo(3));
	}

	private sealed record AppearanceBeforeExtraLabels
	{
		public string? Label { get; init; }

		public string? LabelColor { get; init; }

		public int? FontSize { get; init; }

		public string? TextAlign { get; init; }

		public string? LabelPosition { get; init; }
	}
}
