using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static IEnumerable<Scene> ImageScenes()
	{
		yield return Tile("image",
			new UiStack
			{
				Key = "weather",
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Gap = 0.04,
				Padding = TilePadding,
				Children =
				[
					new UiImage
					{
						Key = "icon",
						Source = Icon("sun", "#ffcc00"),
						Size = 0.16,
						Transition = UiComponentImageTransitions.Crossfade,
					},
					new UiTextRun
					{
						Key = "temp", Text = "23°", Size = 0.2, Weight = UiComponentTextWeights.Bold,
						Align = UiComponentAlignments.Center
					},
					new UiTextRun
					{
						Key = "condition", Text = "Sunny", Size = 0.08, Role = UiComponentTextRoles.Secondary,
						Align = UiComponentAlignments.Center
					},
				],
			});

		var cover = Cover("cover-dim", "#ff5e3a", "#5b2bee");
		yield return Tile("image-dimmed",
			new UiStack
			{
				Key = "covers",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Center,
				Gap = 0.1,
				Padding = TilePadding,
				Children =
				[
					new UiImage { Key = "playing", Source = cover, Size = 0.7 },
					new UiImage { Key = "paused", Source = cover, Size = 0.7, Brightness = 0.6, Saturation = 0.55 },
				],
			},
			2);
	}
}
