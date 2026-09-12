using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static IEnumerable<Scene> SegmentedScenes()
	{
		yield return Tile("segmented",
			new UiStack
			{
				Key = "tile",
				Justify = UiComponentJustify.Center,
				Padding = TilePadding,
				Children =
				[
					new UiSegmented
					{
						Key = "mode",
						MainSize = 0.3,
						Selected = 0,
						Events = [UiEventHandler.On(UiComponentEvents.Change, () => { })],
						Children =
						[
							new UiIcon { Key = "sun", Icon = UiIcons.Sun, Size = 0.14 },
							new UiIcon { Key = "moon", Icon = UiIcons.Moon, Size = 0.14 },
							new UiStack
							{
								Key = "auto",
								Justify = UiComponentJustify.Center,
								Align = UiComponentAlignments.Center,
								Children = [new UiTextRun { Key = "label", Text = "Auto", Size = 0.1 }],
							},
						],
					},
				],
			},
			2);
	}
}
