using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.References;

internal static partial class Scenes
{
	private static UiStack Card(string key, params UiElement[] children) => new()
	{
		Key = key,
		Fill = true,
		Background = "#2c2c2e",
		Children = children,
	};

	private static IEnumerable<Scene> ViewScenes()
	{
		yield return Tile("view-widget", new UiStack
		{
			Key = "weather",
			Padding = TilePadding,
			Justify = UiComponentJustify.SpaceBetween,
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
						new UiStack
						{
							Key = "labels",
							Gap = 0.02,
							Children =
							[
								new UiTextRun { Key = "location", Text = "Berlin", Size = 0.09, Weight = UiComponentTextWeights.SemiBold },
								new UiTextRun { Key = "temp", Text = "21°", Size = 0.26, Weight = UiComponentTextWeights.Bold },
							],
						},
						new UiImage { Key = "icon", Source = Icon("sun", "#ffcc00"), Size = 0.26 },
					],
				},
				new UiTextRun { Key = "condition", Text = "Sunny", Size = 0.08, Role = UiComponentTextRoles.Secondary },
				new UiRangeBar
				{
					Key = "range",
					MainSize = 0.06,
					Thickness = UiSize.FromBasis(0.03, 0.35),
					Start = 0.2,
					End = 0.6,
					StartColor = "#2b6cee",
					EndColor = "#ee2b2b",
					Marker = 0.45,
				},
			],
		}, 2, 2);

		yield return Tile("view-widget-type", new UiLayer
		{
			Key = "gaugeTile",
			Children =
			[
				Gauge(70),
				new UiStack
				{
					Key = "labels",
					Align = UiComponentAlignments.Center,
					Padding = TilePadding,
					Children = [new UiTextRun { Key = "reading", Text = "70 km/h", Size = 0.09, Role = UiComponentTextRoles.Secondary }],
				},
			],
		});

		var berlin = UiValue.Of(UiTimeReference.InZone("Europe/Berlin"));
		yield return Tile("view-folder", new UiStack
		{
			Key = "dashboard",
			Direction = UiComponentDirections.Horizontal,
			Gap = 0.04,
			Padding = 0.04,
			Children =
			[
				Card("clock", new UiStack
				{
					Key = "clockStack",
					Fill = true,
					Padding = 0.06,
					Children = [new UiClockDial { Key = "dial", Value = berlin, Seconds = true, Fill = true }],
				}),
				Card("cpu", HistoryGraph("CPU", "Package", "42 %") with { Fill = true }),
				Card("gpu", HistoryGraph("GPU", "Core", "67 %") with { Fill = true }),
			],
		}, 4, 2);

		IReadOnlyList<(string Id, string Name, string Detail, string Icon)> devices =
		[
			("desk", "Studio desktop", "Windows - this computer", "device-desktop"),
			("tablet", "Living room tablet", "Android - connected", "device-tablet"),
			("phone", "Pixel 9", "Android - last seen 2 min ago", "device-phone"),
		];
		yield return Dialog("view-modal", new UiList
		{
			Key = "devices",
			Gap = UiSize.FromBasis(0.015),
			Padding = UiSize.FromBasis(0.02),
			Children = [.. devices.Select(device => new UiButton
			{
				Key = device.Id,
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Gap = UiSize.FromBasis(0.03),
				Padding = UiSize.FromBasis(0.025),
				Background = "#2c2c2e",
				Events = [UiEventHandler.On(UiComponentEvents.Press, () => { })],
				Children =
				[
					new UiImage { Key = "icon", Source = Icon(device.Icon), Size = UiSize.FromBasis(0.06) },
					new UiStack
					{
						Key = "labels",
						Fill = true,
						Gap = UiSize.FromBasis(0.006),
						Children =
						[
							new UiTextRun { Key = "name", Text = device.Name, Size = UiSize.FromBasis(0.038), Weight = UiComponentTextWeights.Medium },
							new UiTextRun { Key = "detail", Text = device.Detail, Size = UiSize.FromBasis(0.03), Role = UiComponentTextRoles.Secondary },
						],
					},
				],
			})],
		}, 600, 300);
	}
}
