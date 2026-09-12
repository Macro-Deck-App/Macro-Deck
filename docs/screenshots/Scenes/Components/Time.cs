using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.References;

internal static partial class Scenes
{
	// render.html pins the reader's clock to this instant, so a reference anchored here shows its position exactly.
	private static readonly DateTimeOffset Now = new(2026, 9, 12, 14, 5, 30, TimeSpan.Zero);

	private static IEnumerable<Scene> TimeScenes()
	{
		var reference = UiValue.Of(UiTimeReference.InZone("America/New_York"));

		UiDynamicText Clock() => new()
		{
			Key = "time",
			Value = reference,
			Format = UiTimeFormats.Time,
			Seconds = true,
			Size = 0.24,
			MinSize = 0.13,
			Weight = UiComponentTextWeights.Bold,
			Align = UiComponentAlignments.Center,
		};

		yield return Tile("time", Centered("tile", Clock()), 2);

		yield return Tile("time-caption",
			new UiStack
			{
				Key = "tile",
				Justify = UiComponentJustify.Center,
				Gap = 0.04,
				Padding = TilePadding,
				Children =
				[
					Clock(),
					new UiDynamicText
					{
						Key = "caption",
						Value = reference,
						Format = UiTimeFormats.ZoneName,
						Size = 0.09,
						Role = UiComponentTextRoles.Secondary,
						Align = UiComponentAlignments.Center,
					},
				],
			},
			2);

		yield return Tile("clock-dial",
			new UiStack
			{
				Key = "tile",
				Padding = TilePadding,
				Children = [new UiClockDial { Key = "dial", Value = reference, Seconds = true, Fill = true }],
			});
	}

	private static IEnumerable<Scene> ProgressScenes()
	{
		var position = UiValue.Of(UiProgressReference.Advancing(42_000, Now, durationMs: 215_000));
		yield return Tile("progress",
			new UiStack
			{
				Key = "tile",
				Justify = UiComponentJustify.Center,
				Padding = TilePadding,
				Children =
				[
					new UiStack
					{
						Key = "timeline",
						Gap = 0.05,
						Children =
						[
							new UiProgressBar { Key = "progress", Value = position, Thickness = 0.02, MainSize = 0.02 },
							new UiStack
							{
								Key = "times",
								Direction = UiComponentDirections.Horizontal,
								Justify = UiComponentJustify.SpaceBetween,
								Children =
								[
									new UiProgressText
									{
										Key = "elapsed", Value = position, Format = UiProgressFormats.Elapsed,
										Role = UiComponentTextRoles.Muted
									},
									new UiProgressText
									{
										Key = "duration", Value = position, Format = UiProgressFormats.Duration,
										Role = UiComponentTextRoles.Muted, Align = UiComponentAlignments.End
									},
								],
							},
						],
					},
				],
			},
			2);
	}
}
