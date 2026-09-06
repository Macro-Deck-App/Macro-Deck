using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.ConfigFlow;

internal static class WellBehavedConfigFlowPreviews
{
	[UiPreview("Default")]
	public static UiElement Default() => WellBehavedConfigFlow.BuildTree("Berlin, Germany");

	[UiPreview("Empty")]
	public static UiElement Empty() => WellBehavedConfigFlow.BuildTree(string.Empty);

	[UiPreview("Long text")]
	public static UiElement LongText()
		=> WellBehavedConfigFlow.BuildTree(
			"Llanfairpwllgwyngyllgogerychwyrndrobwllllantysiliogogogoch, Isle of Anglesey, Wales");
}
