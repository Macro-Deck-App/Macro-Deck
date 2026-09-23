using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static UiImage Sun(string key, double size) => new() { Key = key, Source = Icon("sun", "#ffcc00"), Size = size };

	private static UiTextRun Temperature(string key, double size, string align = UiComponentAlignments.Start)
		=> new() { Key = key, Text = "21°", Size = size, Weight = UiComponentTextWeights.Bold, Align = align };

	private static UiTextRun Caption(string key, string text, string align = UiComponentAlignments.Start)
		=> new() { Key = key, Text = text, Size = 0.1, Role = UiComponentTextRoles.Secondary, Align = align };

	private static UiResponsive Weather() => new()
	{
		Key = "weather",
		Default = new UiStack
		{
			Key = "compact",
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Padding = TilePadding,
			Children = [Temperature("temp", 0.34, UiComponentAlignments.Center)],
		},
		Variants =
		[
			new UiResponsiveVariant
			{
				MinWidth = 1.5,
				Content = new UiStack
				{
					Key = "wide",
					Direction = UiComponentDirections.Horizontal,
					Align = UiComponentAlignments.Center,
					Gap = 0.08,
					Padding = TilePadding,
					Children =
					[
						Sun("icon", 0.5),
						new UiStack
						{
							Key = "details",
							Fill = true,
							Justify = UiComponentJustify.Center,
							Children = [Temperature("temp", 0.3), Caption("condition", "Sunny")],
						},
					],
				},
			},
			new UiResponsiveVariant
			{
				MaxAspect = 0.67,
				Content = new UiStack
				{
					Key = "tall",
					Justify = UiComponentJustify.Center,
					Align = UiComponentAlignments.Center,
					Gap = 0.06,
					Padding = TilePadding,
					Children =
					[
						Sun("icon", 0.5),
						Temperature("temp", 0.3, UiComponentAlignments.Center),
						Caption("tomorrow", "Tomorrow 18°", UiComponentAlignments.Center),
						Caption("later", "Friday 16°", UiComponentAlignments.Center),
					],
				},
			},
		],
	};

	private static IEnumerable<Scene> ResponsiveScenes()
	{
		yield return DeckTile("responsive-1x1", Weather());
		yield return DeckTile("responsive-2x1", Weather(), 2);
		yield return DeckTile("responsive-1x2", Weather(), 1, 2);
	}
}
