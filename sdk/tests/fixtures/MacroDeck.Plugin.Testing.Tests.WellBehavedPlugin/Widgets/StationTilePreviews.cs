using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Widgets;

internal static class StationTilePreviews
{
	[UiPreview("Station tile", Profile = UiPreviewProfiles.Widget)]
	public static UiElement StationTile()
		=> new UiStack
		{
			Key = "station",
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = 0.04,
			Padding = UiSize.Of(UiLength.Capped(0.1, 12)),
			Children =
			[
				new UiTextRun
				{
					Key = "temperature", Text = "23°", Size = 0.3, Weight = UiComponentTextWeights.Bold,
					Align = UiComponentAlignments.Center
				},
				new UiTextRun
				{
					Key = "station-name", Text = "Primary", Size = 0.1, Role = UiComponentTextRoles.Secondary,
					Align = UiComponentAlignments.Center
				}
			]
		};
}
