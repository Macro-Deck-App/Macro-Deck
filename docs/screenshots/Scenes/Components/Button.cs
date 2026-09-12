using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private const string MicOff = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="-22 -3 68 68" fill="none" stroke="#ffffff" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="m2 2 20 20"/><path d="M18.89 13.23A7.12 7.12 0 0 0 19 12v-2"/><path d="M5 10v2a7 7 0 0 0 12 5"/><path d="M15 9.34V5a3 3 0 0 0-5.68-1.33"/><path d="M9 9v3a3 3 0 0 0 5.12 2.12"/><path d="M12 19v3"/></svg>""";

	private static IEnumerable<Scene> ButtonScenes()
	{
		yield return Tile("button", new UiButton
		{
			Key = "mute",
			Justify = UiComponentJustify.Center,
			Background = "#c62f2f",
			Source = Svg("mic-off", MicOff),
			Fit = UiComponentImageFits.Cover,
			Events = [UiEventHandler.On(UiComponentEvents.Press, () => { })],
			Children = [new UiTextRun { Key = "label", Text = "Mute", Size = 0.14, Align = UiComponentAlignments.Center }],
		});

		yield return Tile("button-artwork", new UiButton
		{
			Key = "cover",
			Source = Cover("cover-button", "#ff2d55", "#ff9500"),
			Fit = UiComponentImageFits.Cover,
			Zoom = 1.2,
			BorderStyle = UiComponentBorderStyles.Static,
			BorderColor = "#ff3b30",
			Events = [UiEventHandler.On(UiComponentEvents.Press, () => { })],
		});
	}
}
