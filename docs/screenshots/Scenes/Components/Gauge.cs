using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static UiStack Padded(UiElement child) => new()
	{
		Key = "tile",
		Padding = TilePadding,
		Children = [child],
	};

	private static IEnumerable<Scene> GaugeScenes()
	{
		yield return Tile("gauge",
			Padded(new UiGauge { Key = "cpu", Fill = true, Level = 0.7, Thickness = 0.08, LevelColor = "#2b6cee" }));

		yield return Tile("gauge-ring",
			Padded(new UiGauge
			{
				Key = "battery",
				Fill = true,
				Level = 0.4,
				StartAngle = 0,
				EndAngle = 360,
				Thickness = 0.1,
				LevelColor = "#34c759",
			}));
	}
}
