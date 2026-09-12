using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private const string GaugeFace = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" fill="none" stroke-linecap="round"><path d="M14 90 A36 36 0 0 1 86 90" stroke="#3a3a3c" stroke-width="7"/><path d="M14 90 A36 36 0 0 1 71.2 60.9" stroke="#2b6cee" stroke-width="7"/></svg>""";
	private const string Needle = """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 100 100" stroke-linecap="round"><path d="M50 90 L50 42" stroke="#ffffff" stroke-width="3"/><circle cx="50" cy="90" r="4.5" fill="#ffffff"/></svg>""";

	private static UiLayer Gauge(double speed) => new()
	{
		Key = "gauge",
		Children =
		[
			new UiImage { Key = "face", Source = Svg("gauge-face", GaugeFace) },
			new UiTransform
			{
				Key = "needle",
				Rotation = speed * 1.8 - 90,
				OriginX = 0.5,
				OriginY = 0.9,
				Children = [new UiImage { Key = "needleArt", Source = Svg("needle", Needle) }],
				Fallback = new UiTextRun { Key = "reading", Text = $"{speed:0} km/h" },
			},
		],
	};

	private static IEnumerable<Scene> TransformScenes()
	{
		yield return Tile("transform", Gauge(70));

		yield return Tile("transform-rotate", Centered("tile", new UiTransform
		{
			Key = "arrow",
			Rotation = 90,
			Children = [new UiImage { Key = "arrowIcon", Source = Icon("arrow-up"), Size = 0.4 }],
		}));
	}

	private static IEnumerable<Scene> ChartScenes()
	{
		yield return Tile("chart", new UiChart
		{
			Key = "chart",
			Points = SamplePoints,
			PlotTop = 0.66,
			Thickness = UiSize.Capped(2d / UiLength.Cell, 2),
		}, 2);

		yield return Tile("chart-color", new UiChart
		{
			Key = "chart",
			Points = SamplePoints,
			Color = "#34c759",
			PlotTop = 0.2,
			Thickness = UiSize.Capped(2d / UiLength.Cell, 2),
		}, 2);
	}
}
