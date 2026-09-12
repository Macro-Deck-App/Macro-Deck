using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static UiStack Reading(string key, string label, string value) => new()
	{
		Key = key,
		Background = "#1c2430",
		Justify = UiComponentJustify.Center,
		Align = UiComponentAlignments.Center,
		Children =
		[
			new UiTextRun { Key = "value", Text = value, Size = 0.14, Weight = UiComponentTextWeights.SemiBold },
			new UiTextRun { Key = "label", Text = label, Size = 0.08, Role = UiComponentTextRoles.Secondary },
		],
	};

	private static IEnumerable<Scene> GridScenes()
	{
		yield return Tile("grid",
			new UiGrid
			{
				Key = "stats",
				Columns = 2,
				Gap = 0.04,
				Padding = TilePadding,
				Children =
				[
					new UiStack
					{
						Key = "cpu",
						Background = "#1c2430",
						ColumnSpan = 2,
						Padding = 0.04,
						Children =
						[
							new UiChart
							{
								Key = "chart",
								Fill = true,
								Points = SamplePoints,
								PlotTop = 0.3,
								Thickness = UiSize.Capped(2d / UiLength.Cell, 2),
							},
						],
					},
					Reading("ram", "RAM", "62 %"),
					Reading("gpu", "GPU", "38 %"),
				],
			},
			2,
			2);
	}
}
