using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;

internal static partial class Scenes
{
	private static IEnumerable<Scene> SliderScenes()
	{
		yield return Tile("slider", new UiStack
		{
			Key = "tile",
			Padding = TilePadding,
			Children =
			[
				new UiSlider
				{
					Key = "volume",
					Fill = true,
					Level = 0.65,
					Step = 0.05,
					Thickness = UiSize.FromBasis(0.12, 0.55),
					Events = [UiEventHandler.On(UiComponentEvents.Change, () => { })],
				},
			],
		}, 2);

		yield return Tile("slider-vertical", new UiStack
		{
			Key = "tile",
			Direction = UiComponentDirections.Horizontal,
			Padding = TilePadding,
			Children =
			[
				new UiSlider
				{
					Key = "volume",
					Fill = true,
					Level = 0.4,
					Direction = UiComponentDirections.Vertical,
					Thickness = UiSize.FromBasis(0.12, 0.55),
					Events = [UiEventHandler.On(UiComponentEvents.Change, () => { })],
				},
			],
		}, 1, 2);
	}

	private static IEnumerable<Scene> RangeBarScenes()
	{
		yield return Tile("range-bar", new UiStack
		{
			Key = "tile",
			Padding = TilePadding,
			Children =
			[
				new UiRangeBar
				{
					Key = "bar",
					Fill = true,
					Thickness = UiSize.FromBasis(0.03, 0.35),
					Start = 0.2,
					End = 0.6,
					StartColor = "#2b6cee",
					EndColor = "#ee2b2b",
					Marker = 0.45,
				},
			],
		}, 2);

		(string Day, string Low, string High, double Start, double End, double? Marker)[] days =
		[
			("Today", "9°", "21°", 0.2, 0.6, 0.45),
			("Tue", "12°", "24°", 0.3, 0.7, null),
			("Wed", "7°", "16°", 0.1, 0.45, null),
		];
		yield return Tile("range-bar-marker", new UiStack
		{
			Key = "forecast",
			Justify = UiComponentJustify.SpaceBetween,
			Padding = TilePadding,
			Children = [.. days.Select(day => new UiStack
			{
				Key = day.Day,
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Gap = 0.04,
				MainSize = 0.2,
				Children =
				[
					new UiTextRun { Key = "day", Text = day.Day, Size = 0.09, MainSize = 0.3 },
					new UiTextRun { Key = "low", Text = day.Low, Size = 0.09, MainSize = 0.14, Role = UiComponentTextRoles.Secondary, Align = UiComponentAlignments.End },
					new UiRangeBar
					{
						Key = "bar",
						Fill = true,
						Thickness = UiSize.FromBasis(0.03, 0.35),
						Start = day.Start,
						End = day.End,
						StartColor = "#2b6cee",
						EndColor = "#ee2b2b",
						Marker = day.Marker is { } marker ? UiValue.Of(marker) : UiValue.None<double>(),
					},
					new UiTextRun { Key = "high", Text = day.High, Size = 0.09, MainSize = 0.16 },
				],
			})],
		}, 2, 1);
	}
}
