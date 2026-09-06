using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Actions;

internal static class SetAlertThresholdActionPreviews
{
	[UiPreview("Default")]
	public static UiElement Default() => SetAlertThresholdAction.BuildTree(30, string.Empty);

	[UiPreview("At the maximum")]
	public static UiElement AtTheMaximum() => SetAlertThresholdAction.BuildTree(40, "signing-secret");

	[UiPreview("Below freezing")]
	public static UiElement BelowFreezing() => SetAlertThresholdAction.BuildTree(-10, string.Empty);
}
