using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static IEnumerable<Scene> ShapeScenes()
	{
		yield return Tile("shape",
			new UiStack
			{
				Key = "tile",
				Padding = 0.3,
				Children =
				[
					new UiShape
					{
						Key = "status",
						Fill = true,
						Shape = UiComponentShapes.Circle,
						Color = "#34c759",
						StrokeColor = "#ffffff",
						StrokeWidth = 0.02,
					},
				],
			});

		yield return Tile("shape-path",
			new UiStack
			{
				Key = "tile",
				Direction = UiComponentDirections.Horizontal,
				Padding = 0.1,
				Gap = 0.1,
				Children =
				[
					new UiShape
					{
						Key = "play",
						Fill = true,
						Shape = UiComponentShapes.Path,
						Path = "M0.2 0.15 L0.85 0.5 L0.2 0.85 Z",
						Color = "#ffffff",
					},
					new UiShape
					{
						Key = "heart",
						Fill = true,
						Shape = UiComponentShapes.Path,
						Path
							= "M0.5 0.85 C0.1 0.6 0.05 0.35 0.2 0.22 C0.33 0.12 0.47 0.2 0.5 0.32 C0.53 0.2 0.67 0.12 0.8 0.22 C0.95 0.35 0.9 0.6 0.5 0.85 Z",
						StrokeColor = "#ff3b30",
						StrokeWidth = 0.02,
					},
				],
			},
			2);
	}
}
