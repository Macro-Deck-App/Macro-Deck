using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	// A stack stretches a container child across its cross axis, so centring on both axes takes one stack per axis.
	private static UiStack Centered(string key, params UiElement[] children) => new()
	{
		Key = key,
		Justify = UiComponentJustify.Center,
		Padding = TilePadding,
		Children =
		[
			new UiStack
			{
				Key = key + "Row",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Children = children,
			},
		],
	};

	private static IEnumerable<Scene> TextScenes()
	{
		yield return Tile("text",
			Centered("tile",
				new UiStack
				{
					Key = "reading",
					Direction = UiComponentDirections.Horizontal,
					Align = UiComponentAlignments.Baseline,
					Gap = 0.02,
					Children =
					[
						new UiTextRun
						{
							Key = "value", Text = "23.4", Size = 0.24, Weight = UiComponentTextWeights.Bold, Digits = 3
						},
						new UiTextRun
						{
							Key = "unit",
							Text = "°C",
							Size = 0.1,
							Weight = UiComponentTextWeights.SemiBold,
							Role = UiComponentTextRoles.Secondary,
						},
					],
				}));
	}

	private static IEnumerable<Scene> TextFieldScenes()
	{
		yield return Dialog("text-field",
			new UiStack
			{
				Key = "picker",
				Padding = UiSize.FromBasis(0.03),
				Children =
				[
					new UiTextField { Key = "search", Placeholder = "Search songs, albums, artists", Size = 0.045 }
				],
			},
			600,
			110);
	}
}
