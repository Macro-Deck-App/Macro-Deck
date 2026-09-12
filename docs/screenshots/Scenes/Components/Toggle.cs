using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static IEnumerable<Scene> ToggleScenes()
	{
		yield return Tile("toggle",
			new UiStack
			{
				Key = "tile",
				Padding = TilePadding,
				Children =
				[
					new UiToggle
					{
						Key = "mute",
						Fill = true,
						On = true,
						LevelColor = "#34c759",
						Size = 0.3,
						Events = [UiEventHandler.On(UiComponentEvents.Change, () => { })],
					},
				],
			});
	}
}
