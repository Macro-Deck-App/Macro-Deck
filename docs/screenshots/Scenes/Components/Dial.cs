using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static IEnumerable<Scene> DialScenes()
	{
		yield return Tile("dial",
			Padded(new UiDial
			{
				Key = "volume",
				Fill = true,
				Level = 0.6,
				Step = 0.05,
				Thickness = 0.08,
				Events = [UiEventHandler.On(UiComponentEvents.Change, () => { })],
			}));
	}
}
