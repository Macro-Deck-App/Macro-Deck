using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.Gauges;

internal static class GaugesWidgetView
{
	private const double FullTurn = 360;
	private const double RingThickness = 0.11;
	private const double PaddedShare = 0.8;
	private const double TitledShare = 0.85;

	private static readonly UiSize _titleSize = UiSize.Capped(0.11, 13);
	private static readonly UiSize _gap = UiSize.Capped(0.02, 3);

	public static UiElement Build(GaugesWidgetData config,
		IReadOnlyList<UiState<GaugeFace>> faces,
		IReadOnlyDictionary<string, UiResource>? icons = null,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius)
	{
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(faces);

		var gauges = config.Shown;

		if (faces.Count != gauges.Count)
		{
			throw new ArgumentException("Every shown gauge needs exactly one face.", nameof(faces));
		}

		var children = new List<UiElement>();

		if (config.Title is { } title)
		{
			children.Add(new UiTextRun
			{
				Key = "title",
				Text = UiText.Of(title),
				Size = _titleSize,
				Weight = UiComponentTextWeights.SemiBold,
				Role = UiComponentTextRoles.Primary,
				MaxLines = 1,
			});
		}

		var resources = icons ?? new Dictionary<string, UiResource>();
		var segments = GaugesLayout.For(gauges.Count);

		children.Add(gauges.Count == 0
			? EmptyHint()
			: segments.Count == 1
				? Layout(segments[0], config, gauges, faces, resources) with { Fill = true }
				: Responsive(segments, config, gauges, faces, resources));

		var content = new UiStack
		{
			Key = "gauges",
			Direction = UiComponentDirections.Vertical,
			Align = UiComponentAlignments.Start,
			Gap = _gap,
			Padding = WidgetSafeArea.For(cornerRadius),
			Background = config.BackgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Children = children,
		};

		return content;
	}

	private static UiStack EmptyHint()
		=> new UiStack
		{
			Key = "emptyHint",
			Fill = true,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Children =
			[
				new UiTextRun
				{
					Key = "emptyText",
					Text = AppStrings.Widgets.Gauges.EmptyHint(),
					Size = UiSize.Capped(0.09, 11),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
					Wrap = true,
				},
			],
		};

	private static UiResponsive Responsive(IReadOnlyList<GaugesLayoutSegment> segments,
		GaugesWidgetData config,
		IReadOnlyList<GaugeConfig> gauges,
		IReadOnlyList<UiState<GaugeFace>> faces,
		IReadOnlyDictionary<string, UiResource> icons)
	{
		var main = segments.FirstOrDefault(segment => segment.Contains(1)) ?? segments[0];

		var others = segments
			.Where(segment => segment != main)
			.Select(segment => new UiResponsiveVariant
			{
				MinAspect = segment.MinAspect,
				MaxAspect = segment.MaxAspect,
				Content = Layout(segment, config, gauges, faces, icons),
			})
			.ToList();

		return new UiResponsive
		{
			Key = "layout",
			Fill = true,
			Default = Layout(main, config, gauges, faces, icons),
			Variants = others,
		};
	}

	private static UiGrid Layout(GaugesLayoutSegment segment,
		GaugesWidgetData config,
		IReadOnlyList<GaugeConfig> gauges,
		IReadOnlyList<UiState<GaugeFace>> faces,
		IReadOnlyDictionary<string, UiResource> icons)
	{
		var prefix = $"c{segment.Columns}";
		var scale = segment.RingScale * PaddedShare * (config.Title is null ? 1 : TitledShare);

		return new UiGrid
		{
			Key = $"{prefix}-grid",
			Columns = segment.Columns,
			Rows = segment.Rows,
			Gap = UiSize.FromBasis(0.12 * scale),
			Children = gauges
				.Select((gauge, index) => Cell($"g{index}", scale, config, gauge, faces[index], icons))
				.ToList(),
		};
	}

	private static UiStack Cell(string key,
		double scale,
		GaugesWidgetData config,
		GaugeConfig gauge,
		UiState<GaugeFace> face,
		IReadOnlyDictionary<string, UiResource> icons)
	{
		var children = new List<UiElement>
		{
			new UiLayer
			{
				Key = "dial",
				Fill = true,
				Children = [Ring(scale, config, face), Centre(scale, gauge, icons)],
			},
			new UiStack
			{
				Key = "reading",
				Direction = UiComponentDirections.Horizontal,
				Justify = UiComponentJustify.Center,
				Align = UiComponentAlignments.Baseline,
				Gap = UiSize.FromBasis(0.015 * scale),
				Children =
				[
					new UiTextRun
					{
						Key = "value",
						Text = UiText.From(() => face.Value.Value),
						Size = TextSize(0.25 * scale, null, 17),
						Weight = UiComponentTextWeights.SemiBold,
						Role = UiComponentTextRoles.Primary,
						MaxLines = 1,
					},
					new UiWhen
					{
						Key = "unitGate",
						Condition = () => face.Value.HasUnit,
						Content = () => new UiTextRun
						{
							Key = "unit",
							Text = UiText.Optional(() => face.Value.Unit),
							Size = TextSize(0.17 * scale, null, 12),
							Weight = UiComponentTextWeights.SemiBold,
							Role = UiComponentTextRoles.Secondary,
							MaxLines = 1,
						},
					},
				],
			},
		};

		children.Add(new UiWhen
		{
			Key = "nameGate",
			Condition = () => face.Value.Name.Length > 0,
			Content = () => new UiTextRun
			{
				Key = "name",
				Text = UiText.From(() => face.Value.Name),
				Size = TextSize(0.2 * scale, 0.3, 12),
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
				MaxLines = 1,
			},
		});

		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Children = children,
		};
	}

	private static UiGauge Ring(double scale, GaugesWidgetData config, UiState<GaugeFace> face)
		=> new()
		{
			Key = "ring",
			Fill = true,
			Thickness = UiSize.FromBasis(RingThickness * scale),
			StartAngle = config.IsArc ? UiValue.None<double>() : UiValue.Of(0d),
			EndAngle = config.IsArc ? UiValue.None<double>() : UiValue.Of(FullTurn),
			Level = UiValue.From(() => face.Value.Level),
			LevelColor = UiValue.Optional(() => face.Value.Color is { } color
				? UiValue.Of(color)
				: UiValue.None<string>()),
			Fallback = new UiRangeBar
			{
				Key = "ringFallback",
				Thickness = UiSize.FromBasis(0.05 * scale),
				Start = 0d,
				End = UiValue.From(() => face.Value.Level),
			},
		};

	private static UiStack Centre(double scale,
		GaugeConfig gauge,
		IReadOnlyDictionary<string, UiResource> icons)
	{
		// Horizontal, so the clamp below measures the ring's height rather than the cell's width.
		var size = UiSize.FromBasis(0.36 * scale, maxOfCross: 0.36);

		UiElement? glyph = null;

		if (icons.TryGetValue(gauge.Id, out var resource))
		{
			// Only a button recolours artwork; without events it draws and takes no input.
			glyph = gauge.IconColor is { } tint
				? new UiButton { Key = "icon", Source = resource, Tint = tint, MainSize = size, Background = "transparent" }
				: new UiImage { Key = "icon", Source = resource, Size = size };
		}

		return new UiStack
		{
			Key = "centre",
			Direction = UiComponentDirections.Horizontal,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Children = glyph is null ? [] : [glyph],
		};
	}

	private static UiSize TextSize(double basis, double? maxOfCross, double cap)
		=> UiSize.Of(new UiLength { Basis = basis, MaxOfCross = maxOfCross, MaxOfCell = cap / UiLength.Cell });
}
