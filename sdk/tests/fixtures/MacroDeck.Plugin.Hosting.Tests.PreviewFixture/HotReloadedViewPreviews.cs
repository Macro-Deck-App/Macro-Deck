using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Previews;

namespace MacroDeck.Plugin.Hosting.Tests.PreviewFixture;

// Kept out of the test assembly itself: the SDK scans every registered integration's assembly for
// scenarios, so one declared there would give every plugin built by those tests a ui capability.
public static class HotReloadedViewPreviews
{
	public const string View = "HotReloadedView";

	public static string Text { get; set; } = "before";

	public static bool Throws { get; set; }

	[UiPreview("Default", View = View)]
	public static UiTextRun Default()
		=> Throws
			? throw new InvalidOperationException("The edited scenario does not build.")
			: new UiTextRun { Key = "marker", Text = UiText.Of(Text) };
}
