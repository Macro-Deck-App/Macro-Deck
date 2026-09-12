using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static UiStack RoomCard() => new()
	{
		Key = "card",
		Justify = UiComponentJustify.SpaceBetween,
		Padding = TilePadding,
		Children =
		[
			new UiStack
			{
				Key = "header",
				Direction = UiComponentDirections.Horizontal,
				Children =
				[
					new UiTextRun { Key = "room", Text = "Office", Size = 0.1, Fill = true },
					new UiTextRun
					{
						Key = "time", Text = "14:05", Size = 0.1, MainSize = 0.3, Align = UiComponentAlignments.End,
						Role = UiComponentTextRoles.Secondary
					},
				],
			},
			new UiTextRun { Key = "value", Text = "21.5°", Size = 0.3, Weight = UiComponentTextWeights.Bold },
			// Scenes draw a cell at twice the reference size, so a cap in reference units is scaled to match.
			new UiTextRun
			{
				Key = "caption", Text = "Humidity 48 %", Size = UiSize.Capped(0.1, 12 * Scale),
				Role = UiComponentTextRoles.Secondary
			},
		],
	};

	private static UiStack NameAndValue(bool sized) => new()
	{
		Key = "tile",
		Justify = UiComponentJustify.Center,
		Padding = TilePadding,
		Children =
		[
			new UiStack
			{
				Key = "row",
				Direction = UiComponentDirections.Horizontal,
				Children =
				[
					new UiTextRun { Key = "name", Text = "Living room", Size = 0.14, Fill = true },
					sized
						? new UiTextRun
						{
							Key = "temp", Text = "21°", Size = 0.14, MainSize = 0.3, Align = UiComponentAlignments.End
						}
						: new UiTextRun { Key = "temp", Text = "21°", Size = 0.14, Align = UiComponentAlignments.End },
				],
			},
		],
	};

	private static UiStack StatusCard() => new()
	{
		Key = "status",
		Justify = UiComponentJustify.Center,
		Gap = 0.03,
		Padding = TilePadding,
		Children =
		[
			new UiTextRun { Key = "name", Text = "Studio mic", Size = 0.12, Weight = UiComponentTextWeights.SemiBold },
			new UiTextRun { Key = "state", Text = "Live", Size = 0.1, Color = "#34c759" },
			new UiTextRun
			{
				Key = "source", Text = "Input 2 - USB interface", Size = 0.08, Role = UiComponentTextRoles.Secondary
			},
			new UiTextRun
				{ Key = "updated", Text = "Updated 2 min ago", Size = 0.07, Role = UiComponentTextRoles.Muted },
			new UiSlider
			{
				Key = "level", Level = 0.6, MainSize = 0.16, Thickness = UiSize.FromBasis(0.06),
				Events = [UiEventHandler.On(UiComponentEvents.Change, () => { })]
			},
		],
	};

	private static IEnumerable<Scene> ConceptScenes()
	{
		yield return Tile("sizing-1x1", RoomCard());
		yield return Tile("sizing-2x1", RoomCard(), 2);
		yield return Tile("sizing-2x2", RoomCard(), 2, 2);
		yield return Tile("sizing-fill-wrong", NameAndValue(false), 2);
		yield return Tile("sizing-fill-right", NameAndValue(true), 2);
		yield return Tile("theming", StatusCard(), 2);
		yield return Tile("theming-light", StatusCard(), 2);
	}
}
