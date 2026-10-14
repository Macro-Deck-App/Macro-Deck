// ReSharper disable CheckNamespace

using MacroDeck.Ui.Components;

internal static partial class Scenes
{
	private static IEnumerable<Scene> IconScenes()
	{
		yield return Tile("icon",
			new UiStack
			{
				Key = "tile",
				Padding = TilePadding,
				Children = [new UiIcon { Key = "state", Fill = true, Icon = UiIcons.Play, Size = 0.4 }]
			});
	}
}
