using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Widgets;

internal static class LocalizedLabelPreviews
{
	[UiPreview("Localized label", Profile = UiPreviewProfiles.Widget)]
	public static UiElement LocalizedLabel()
		=> new UiStack
		{
			Key = "localized",
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = 0.04,
			Children =
			[
				new UiTextRun
				{
					Key = "connect", Text = UiText.FromLocalized(() => Strings.Connect()), Size = 0.16,
					Align = UiComponentAlignments.Center
				},
				new UiTextRun
				{
					Key = "devices", Text = UiText.FromLocalized(() => Strings.DeviceCount(3)), Size = 0.08,
					Role = UiComponentTextRoles.Secondary, Align = UiComponentAlignments.Center
				}
			]
		};
}
