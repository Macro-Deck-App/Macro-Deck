using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static IEnumerable<Scene> IconScenes()
	{
		yield return Tile("icon",
			new UiStack
			{
				Key = "tile",
				Padding = TilePadding,
				Children = [new UiIcon { Key = "state", Fill = true, Icon = UiIcons.Play, Size = 0.4 }],
			});
	}
}
