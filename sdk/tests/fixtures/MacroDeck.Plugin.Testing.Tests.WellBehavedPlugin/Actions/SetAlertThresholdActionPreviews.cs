using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Actions;

internal static class SetAlertThresholdActionPreviews
{
	[UiPreview("Default")]
	public static UiElement Default()
	{
		return SetAlertThresholdAction.BuildTree(30, string.Empty);
	}

	[UiPreview("At the maximum")]
	public static UiElement AtTheMaximum()
	{
		return SetAlertThresholdAction.BuildTree(40, "signing-secret");
	}

	[UiPreview("Below freezing")]
	public static UiElement BelowFreezing()
	{
		return SetAlertThresholdAction.BuildTree(-10, string.Empty);
	}
}
