using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static UiStack TrackRow(string key, string title, string subtitle, string from, string to) => new()
	{
		Key = key,
		Direction = UiComponentDirections.Horizontal,
		Align = UiComponentAlignments.Center,
		Gap = UiSize.FromBasis(0.025),
		Padding = UiSize.FromBasis(0.02),
		Children =
		[
			new UiImage { Key = "art", Source = Cover("cover-" + key, from, to), Size = UiSize.FromBasis(0.09) },
			new UiStack
			{
				Key = "labels",
				Fill = true,
				Justify = UiComponentJustify.Center,
				Gap = UiSize.FromBasis(0.004),
				Children =
				[
					new UiTextRun { Key = "title", Text = title, Size = UiSize.FromBasis(0.038), Weight = UiComponentTextWeights.Medium },
					new UiTextRun { Key = "subtitle", Text = subtitle, Size = UiSize.FromBasis(0.032), Role = UiComponentTextRoles.Secondary },
				],
			},
		],
	};

	private static IEnumerable<Scene> StackScenes()
	{
		yield return Dialog("stack", new UiStack
		{
			Key = "dialog",
			Justify = UiComponentJustify.Center,
			Padding = UiSize.FromBasis(0.02),
			Children = [TrackRow("row", "Blue in Green", "Miles Davis - Kind of Blue", "#2b6cee", "#1b2a4a")],
		}, 600, 110);

		yield return Tile("stack-fill", new UiStack
		{
			Key = "tile",
			Justify = UiComponentJustify.Center,
			Padding = TilePadding,
			Children =
			[
				new UiStack
				{
					Key = "header",
					Direction = UiComponentDirections.Horizontal,
					Children =
					[
						new UiTextRun { Key = "name", Text = "Office", Size = 0.12, Fill = true },
						new UiTextRun { Key = "temp", Text = "21°", Size = 0.12, MainSize = 0.3, Align = UiComponentAlignments.End },
					],
				},
			],
		}, 2);

		yield return Tile("stack-space-between", new UiStack
		{
			Key = "tile",
			Justify = UiComponentJustify.Center,
			Padding = TilePadding,
			Children =
			[
				new UiStack
				{
					Key = "current",
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.SpaceBetween,
					Align = UiComponentAlignments.Center,
					Children =
					[
						new UiImage { Key = "icon", Source = Icon("sun", "#ffcc00"), Size = 0.3 },
						new UiTextRun { Key = "temp", Text = "21°", Size = 0.24, Weight = UiComponentTextWeights.Bold },
					],
				},
			],
		}, 2);

		yield return Tile("stack-baseline", Centered("tile", new UiStack
		{
			Key = "reading",
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Baseline,
			Children =
			[
				new UiTextRun { Key = "value", Text = "73", Size = 0.3 },
				new UiTextRun { Key = "unit", Text = "km/h", Size = 0.1 },
			],
		}));

		yield return Tile("layer", HistoryGraph("CPU", "Package", "42 %"), 2);
	}

	private static UiLayer HistoryGraph(string title, string subtitle, string value) => new()
	{
		Key = "historyGraph",
		Children =
		[
			new UiChart { Key = "chart", Points = SamplePoints, PlotTop = 0.66, Thickness = UiSize.Capped(2d / UiLength.Cell, 2) },
			new UiStack
			{
				Key = "labels",
				Padding = TilePadding,
				Gap = 0.01,
				Children =
				[
					new UiTextRun { Key = "title", Text = title, Size = 0.1, Weight = UiComponentTextWeights.SemiBold },
					new UiTextRun { Key = "subtitle", Text = subtitle, Size = 0.07, Role = UiComponentTextRoles.Muted },
				],
			},
			new UiStack
			{
				Key = "value",
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Children = [new UiTextRun { Key = "reading", Text = value, Size = 0.22, Weight = UiComponentTextWeights.Bold, Align = UiComponentAlignments.Center }],
			},
		],
	};

	private static UiValue<IReadOnlyList<double>> SamplePoints => UiValue.Of<IReadOnlyList<double>>(
		[0.32, 0.35, 0.3, 0.42, 0.55, 0.48, 0.5, 0.62, 0.58, 0.44, 0.4, 0.47, 0.6, 0.72, 0.66, 0.52, 0.49, 0.56, 0.63, 0.58]);
}
