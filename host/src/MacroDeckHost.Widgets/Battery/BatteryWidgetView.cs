using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Sdk.Batteries;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Gauges;

namespace MacroDeckHost.Widgets.Battery;

internal static class BatteryWidgetView
{
	public const string UnknownLevel = "--";

	private const string Percent = "%";
	private const double FullTurn = 360;
	private const double RingThickness = 0.11;
	private const double PaddedShare = 0.8;
	private const double TallAspect = 0.6;
	private const double WideAspect = 1.6;
	private const double WideRowsAspect = 1.8;
	private const int IconVersion = 2;

	private static readonly UiSize _gap = UiSize.Capped(0.02, 3);

	public static UiElement Build(
		UiState<BatteryViewState> state,
		BatteryWidgetData config,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(config);

		return new UiStack
		{
			Key = "battery",
			Direction = UiComponentDirections.Vertical,
			Gap = _gap,
			Padding = WidgetSafeArea.For(cornerRadius),
			Background = config.BackgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Children =
			[
				new UiRepeat<BatteryViewState>
				{
					Key = "content",
					Items = UiValue.From(() => (IReadOnlyList<BatteryViewState>)[state.Value]),
					KeySelector = ShapeOf,
					Template = (view, _) => Content(view, config)
				}
			]
		};
	}

	internal static string IconOf(string kind)
	{
		return kind switch
		{
			BatteryDeviceKinds.Phone => UiIcons.DevicePhone,
			BatteryDeviceKinds.Tablet => UiIcons.DeviceTablet,
			BatteryDeviceKinds.Laptop => UiIcons.DeviceLaptop,
			BatteryDeviceKinds.Headphones => UiIcons.DeviceHeadphones,
			BatteryDeviceKinds.Watch => UiIcons.DeviceWatch,
			BatteryDeviceKinds.Controller => UiIcons.DeviceGamepad,
			BatteryDeviceKinds.Mouse => UiIcons.DeviceMouse,
			BatteryDeviceKinds.Keyboard => UiIcons.DeviceKeyboard,
			BatteryDeviceKinds.Speaker => UiIcons.DeviceSpeaker,
			_ => UiIcons.Battery
		};
	}

	internal static string FallbackIconOf(string kind)
	{
		return kind switch
		{
			BatteryDeviceKinds.Phone or BatteryDeviceKinds.Watch => UiIcons.DevicePhone,
			BatteryDeviceKinds.Tablet => UiIcons.DeviceTablet,
			BatteryDeviceKinds.Laptop
				or BatteryDeviceKinds.Mouse
				or BatteryDeviceKinds.Keyboard
				or BatteryDeviceKinds.Controller
				=> UiIcons.DeviceDesktop,
			BatteryDeviceKinds.Headphones or BatteryDeviceKinds.Speaker => UiIcons.MusicNote,
			_ => UiIcons.Zap
		};
	}

	internal static string PercentOf(BatteryFace face)
	{
		return face.Level is { } level && face.Status != BatteryFaceStatus.NotAvailable
			? level.ToString(CultureInfo.InvariantCulture)
			: UnknownLevel;
	}

	internal static LocalizedText StateOf(BatteryFace face)
	{
		switch (face.Status)
		{
			case BatteryFaceStatus.NotAvailable:
				return AppStrings.Widgets.Battery.State.NotAvailable();
			case BatteryFaceStatus.NotConnected:
				return AppStrings.Widgets.Battery.State.NotConnected();
		}

		return face.ChargeState switch
		{
			BatteryChargeState.Charging => face.MinutesToFull is { } toFull
				? toFull >= 60
					? AppStrings.Widgets.Battery.State.FullInHours(Number(toFull / 60), Number(toFull % 60))
					: AppStrings.Widgets.Battery.State.FullInMinutes(Number(toFull))
				: AppStrings.Widgets.Battery.State.Charging(),
			BatteryChargeState.Full => AppStrings.Widgets.Battery.State.Full(),
			BatteryChargeState.NotCharging => AppStrings.Widgets.Battery.State.NotCharging(),
			BatteryChargeState.Discharging when face.MinutesRemaining is { } left => left >= 60
				? AppStrings.Widgets.Battery.State.HoursLeft(Number(left / 60), Number(left % 60))
				: AppStrings.Widgets.Battery.State.MinutesLeft(Number(left)),
			_ => default
		};
	}

	private static LocalizedText Number(int value)
	{
		return LocalizedText.FromLiteral(value.ToString(CultureInfo.InvariantCulture));
	}

	private static string ShapeOf(BatteryViewState view)
	{
		return view.Hint != BatteryViewHint.None
			? "hint"
			: view.Faces.Count == 1
				? "single"
				: $"multi{view.Faces.Count.ToString(CultureInfo.InvariantCulture)}";
	}

	private static UiElement Content(BatteryViewState view, BatteryWidgetData config)
	{
		if (view.Hint != BatteryViewHint.None || view.Faces.Count == 0)
		{
			return Hint(view.Hint == BatteryViewHint.PickDevice
				? AppStrings.Widgets.Battery.PickDeviceHint()
				: AppStrings.Widgets.Battery.EmptyHint());
		}

		if (view.Faces.Count == 1)
		{
			return config.IsBar ? SingleBar(view.Faces[0], config) : SingleRing(view.Faces[0], config);
		}

		return config.IsBar ? BarRows(view.Faces, config) : RingGrid(view.Faces, config);
	}

	private static UiStack Hint(LocalizedText text)
	{
		return new UiStack
		{
			Key = "hint",
			Fill = true,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Children =
			[
				new UiTextRun
				{
					Key = "hintText",
					Text = text,
					Size = UiSize.Capped(0.09, 11),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
					Wrap = true
				}
			]
		};
	}

	private static UiResponsive SingleRing(BatteryFace face, BatteryWidgetData config)
	{
		var scale = GaugesLayout.For(1)[0].RingScale * PaddedShare;

		return new UiResponsive
		{
			Key = "single",
			Fill = true,
			Default = Cell("square", scale, face, config),
			Variants =
			[
				new UiResponsiveVariant { MinAspect = WideAspect, Content = SingleWide(face, config) },
				new UiResponsiveVariant { MaxAspect = TallAspect, Content = SingleTall(face, config) }
			]
		};
	}

	private static UiStack SingleWide(BatteryFace face, BatteryWidgetData config)
	{
		const double scale = 0.85;

		return new UiStack
		{
			Key = "wide",
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = UiSize.FromBasis(0.14),
			Children =
			[
				new UiLayer
				{
					Key = "dial",
					MainSize = UiSize.FromBasis(1),
					Children = [Ring(scale, face), Centre(scale, face)]
				},
				Details(face, config, 0.17, 0.36, 0.15, UiComponentAlignments.Start) with
				{
					Fill = true
				}
			]
		};
	}

	private static UiStack SingleTall(BatteryFace face, BatteryWidgetData config)
	{
		const double scale = 0.8;

		return new UiStack
		{
			Key = "tall",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = UiSize.FromBasis(0.1),
			Children =
			[
				new UiLayer
				{
					Key = "dial",
					MainSize = UiSize.FromBasis(0.9),
					Children = [Ring(scale, face), Centre(scale, face)]
				},
				Details(face, config, 0.15, 0.3, 0.12, UiComponentAlignments.Center)
			]
		};
	}

	private static UiStack Details(
		BatteryFace face,
		BatteryWidgetData config,
		double nameSize,
		double valueSize,
		double stateSize,
		string align)
	{
		var children = new List<UiElement>();

		if (config.ShowName && face.Name.Length > 0)
		{
			children.Add(new UiTextRun
			{
				Key = "name",
				Text = UiText.Of(face.Name),
				Size = TextSize(nameSize, null, 15),
				Weight = UiComponentTextWeights.SemiBold,
				Role = UiComponentTextRoles.Primary,
				Align = align,
				MaxLines = 1
			});
		}

		if (config.ShowPercentage)
		{
			children.Add(Reading(face, config, valueSize, valueSize * 0.6, 30, align));
		}

		var state = StateOf(face);

		if (!state.IsEmpty)
		{
			children.Add(new UiTextRun
			{
				Key = "state",
				Text = state,
				Size = TextSize(stateSize, null, 13),
				Role = UiComponentTextRoles.Muted,
				Align = align,
				MaxLines = 1
			});
		}

		return new UiStack
		{
			Key = "details",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = align,
			Gap = UiSize.FromBasis(0.03),
			Children = children
		};
	}

	private static UiStack SingleBar(BatteryFace face, BatteryWidgetData config)
	{
		var header = new List<UiElement> { Glyph(UiSize.FromBasis(0.2, 1), face) };

		if (config.ShowName && face.Name.Length > 0)
		{
			header.Add(new UiTextRun
			{
				Key = "name",
				Fill = true,
				Text = UiText.Of(face.Name),
				Size = TextSize(0.15, null, 15),
				Weight = UiComponentTextWeights.SemiBold,
				Role = UiComponentTextRoles.Primary,
				MaxLines = 1
			});
		}

		var children = new List<UiElement>
		{
			new UiStack
			{
				Key = "header",
				Direction = UiComponentDirections.Horizontal,
				Align = UiComponentAlignments.Center,
				Gap = UiSize.FromBasis(0.05),
				Children = header
			}
		};

		if (config.ShowPercentage)
		{
			children.Add(Reading(face, config, 0.3, 0.18, 26, UiComponentAlignments.Start));
		}

		children.Add(Bar(face, UiSize.FromBasis(0.09)));

		var state = StateOf(face);

		if (!state.IsEmpty)
		{
			children.Add(new UiTextRun
			{
				Key = "state",
				Text = state,
				Size = TextSize(0.12, null, 12),
				Role = UiComponentTextRoles.Muted,
				MaxLines = 1
			});
		}

		return new UiStack
		{
			Key = "singleBar",
			Fill = true,
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Gap = UiSize.FromBasis(0.06),
			Children = children
		};
	}

	private static UiResponsive RingGrid(IReadOnlyList<BatteryFace> faces, BatteryWidgetData config)
	{
		var segments = GaugesLayout.For(faces.Count)
			.Where(segment => segment.MaxAspect is not { } max || max > TallAspect)
			.ToList();
		var main = segments.FirstOrDefault(segment => segment.Contains(1)) ?? segments[0];

		var variants = new List<UiResponsiveVariant>
		{
			new() { MaxAspect = TallAspect, Content = Rows("tall", 1, faces, config) }
		};

		if (faces.Count >= 3)
		{
			variants.Add(new UiResponsiveVariant
			{
				MinWidth = 2,
				MinHeight = 2,
				Content = Rows("large", 1, faces, config)
			});
		}

		variants.AddRange(segments
			.Where(segment => segment != main)
			.Select(segment => new UiResponsiveVariant
			{
				MinAspect = segment.MinAspect,
				MaxAspect = segment.MaxAspect,
				Content = Grid(segment, faces, config)
			}));

		return new UiResponsive
		{
			Key = "rings",
			Fill = true,
			Default = Grid(main, faces, config),
			Variants = variants
		};
	}

	private static UiGrid Grid(GaugesLayoutSegment segment, IReadOnlyList<BatteryFace> faces, BatteryWidgetData config)
	{
		var scale = segment.RingScale * PaddedShare;

		return new UiGrid
		{
			Key = $"c{segment.Columns.ToString(CultureInfo.InvariantCulture)}-grid",
			Columns = segment.Columns,
			Rows = segment.Rows,
			Gap = UiSize.FromBasis(0.12 * scale),
			Children =
			[
				.. faces.Select((face, index)
					=> Cell($"d{index.ToString(CultureInfo.InvariantCulture)}", scale, face, config))
			]
		};
	}

	private static UiStack Cell(string key, double scale, BatteryFace face, BatteryWidgetData config)
	{
		var children = new List<UiElement>
		{
			new UiLayer
			{
				Key = "dial",
				Fill = true,
				Children = [Ring(scale, face), Centre(scale, face)]
			}
		};

		if (config.ShowPercentage)
		{
			children.Add(Reading(face, config, 0.25 * scale, 0.17 * scale, 17, UiComponentAlignments.Center));
		}

		if (config.ShowName && face.Name.Length > 0)
		{
			children.Add(new UiTextRun
			{
				Key = "name",
				Text = UiText.Of(face.Name),
				Size = TextSize(0.2 * scale, 0.3, 12),
				Role = UiComponentTextRoles.Muted,
				Align = UiComponentAlignments.Center,
				MaxLines = 1
			});
		}

		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Children = children
		};
	}

	private static UiResponsive BarRows(IReadOnlyList<BatteryFace> faces, BatteryWidgetData config)
	{
		return new UiResponsive
		{
			Key = "bars",
			Fill = true,
			Default = Rows("narrow", 1, faces, config),
			Variants =
			[
				new UiResponsiveVariant { MinAspect = WideRowsAspect, Content = Rows("wide", 2, faces, config) }
			]
		};
	}

	private static UiGrid Rows(string prefix, int columns, IReadOnlyList<BatteryFace> faces, BatteryWidgetData config)
	{
		var rows = GaugesLayout.RowsFor(faces.Count, columns);

		return new UiGrid
		{
			Key = $"{prefix}-rows",
			Columns = columns,
			Rows = rows,
			Gap = UiSize.FromBasis(0.04),
			Children =
			[
				.. faces.Select((face, index) => Row($"d{index.ToString(CultureInfo.InvariantCulture)}", face, config))
			]
		};
	}

	private static UiStack Row(string key, BatteryFace face, BatteryWidgetData config)
	{
		var top = new List<UiElement>
		{
			new UiTextRun
			{
				Key = "name",
				Fill = true,
				Text = config.ShowName && face.Name.Length > 0 ? UiText.Of(face.Name) : UiText.None(),
				Size = TextSize(0.09, 0.32, 12),
				Weight = UiComponentTextWeights.Medium,
				Role = UiComponentTextRoles.Primary,
				MaxLines = 1
			}
		};

		if (face.Status == BatteryFaceStatus.Live)
		{
			var reading = new List<UiElement>();

			if (Bolt(face, config, TextSize(0.08, 0.3, 11)) is { } bolt)
			{
				reading.Add(bolt);
			}

			if (config.ShowPercentage)
			{
				reading.Add(new UiTextRun
				{
					Key = "value",
					Text = UiText.Of(face.Level is null ? UnknownLevel : PercentOf(face) + Percent),
					Size = TextSize(0.09, 0.32, 12),
					Weight = UiComponentTextWeights.SemiBold,
					Role = UiComponentTextRoles.Primary,
					MaxLines = 1
				});
			}

			if (reading.Count > 0)
			{
				top.Add(new UiStack
				{
					Key = "reading",
					MainSize = UiSize.FromBasis(0.34),
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.End,
					Align = UiComponentAlignments.Center,
					Gap = UiSize.FromBasis(0.01),
					Children = reading
				});
			}
		}

		return new UiStack
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = UiSize.FromBasis(0.04),
			Children =
			[
				Glyph(UiSize.FromBasis(0.11, 0.45), face),
				new UiStack
				{
					Key = "line",
					Fill = true,
					Direction = UiComponentDirections.Vertical,
					Justify = UiComponentJustify.Center,
					Gap = UiSize.FromBasis(0.025),
					Children =
					[
						new UiStack
						{
							Key = "top",
							Direction = UiComponentDirections.Horizontal,
							Align = UiComponentAlignments.Center,
							Gap = UiSize.FromBasis(0.015),
							Children = top
						},
						face.Status == BatteryFaceStatus.Live
							? Bar(face, UiSize.FromBasis(0.04))
							: new UiTextRun
							{
								Key = "state",
								Text = StateOf(face),
								Size = TextSize(0.08, 0.26, 10),
								Role = UiComponentTextRoles.Muted,
								MaxLines = 1
							}
					]
				}
			]
		};
	}

	private static UiStack Reading(
		BatteryFace face,
		BatteryWidgetData config,
		double valueSize,
		double unitSize,
		double cap,
		string justify)
	{
		var children = new List<UiElement>();

		if (Bolt(face, config, TextSize(unitSize, null, cap * 0.6)) is { } bolt)
		{
			children.Add(bolt);
		}

		children.Add(new UiTextRun
		{
			Key = "value",
			Text = UiText.Of(PercentOf(face)),
			Size = TextSize(valueSize, null, cap),
			Weight = UiComponentTextWeights.SemiBold,
			Role = face.IsMuted ? UiComponentTextRoles.Secondary : UiComponentTextRoles.Primary,
			MaxLines = 1
		});

		if (face.Level is not null && face.Status != BatteryFaceStatus.NotAvailable)
		{
			children.Add(new UiTextRun
			{
				Key = "unit",
				Text = UiText.Of(Percent),
				Size = TextSize(unitSize, null, cap * 0.6),
				Weight = UiComponentTextWeights.SemiBold,
				Role = UiComponentTextRoles.Secondary,
				MaxLines = 1
			});
		}

		return new UiStack
		{
			Key = "reading",
			Direction = UiComponentDirections.Horizontal,
			Justify = justify == UiComponentAlignments.Start ? UiComponentJustify.Start : UiComponentJustify.Center,
			Align = UiComponentAlignments.Baseline,
			Gap = UiSize.FromBasis(0.01),
			Children = children
		};
	}

	private static UiIcon? Bolt(BatteryFace face, BatteryWidgetData config, UiSize size)
	{
		return config.ShowChargingBadge && face.IsCharging
			? new UiIcon
			{
				Key = "bolt",
				Icon = UiIcons.Zap,
				Size = size,
				MainSize = size,
				Color = face.Color is { } color ? UiValue.Of(color) : UiValue.None<string>(),
				Role = UiComponentTextRoles.Primary
			}
			: null;
	}

	private static UiGauge Ring(double scale, BatteryFace face)
	{
		return new UiGauge
		{
			Key = "ring",
			Fill = true,
			Thickness = UiSize.FromBasis(RingThickness * scale),
			StartAngle = 0d,
			EndAngle = FullTurn,
			Level = face.Fill,
			LevelColor = face.Color is { } color ? UiValue.Of(color) : UiValue.None<string>(),
			Fallback = new UiRangeBar
			{
				Key = "ringFallback",
				Thickness = UiSize.FromBasis(0.05 * scale),
				Start = 0d,
				End = face.Fill
			}
		};
	}

	private static UiStack Centre(double scale, BatteryFace face)
	{
		return new UiStack
		{
			Key = "centre",
			Direction = UiComponentDirections.Horizontal,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Children = [Glyph(UiSize.FromBasis(0.36 * scale, 0.36), face)]
		};
	}

	private static UiIcon Glyph(UiSize size, BatteryFace face)
	{
		var role = face.IsMuted ? UiComponentTextRoles.Muted : UiComponentTextRoles.Primary;

		return new UiIcon
		{
			Key = "glyph",
			Icon = IconOf(face.Kind),
			Size = size,
			MainSize = size,
			Role = role,
			// The device glyphs arrived in the second ui.icon group; an older reader draws the nearest
			// first-group glyph instead.
			RequiredComponentVersion = IconVersion,
			Fallback = new UiIcon
			{
				Key = "glyphFallback",
				Icon = FallbackIconOf(face.Kind),
				Size = size,
				MainSize = size,
				Role = role
			}
		};
	}

	private static UiRangeBar Bar(BatteryFace face, UiSize thickness)
	{
		return new UiRangeBar
		{
			Key = "bar",
			Thickness = thickness,
			Start = 0d,
			End = face.Fill,
			StartColor = face.Color is { } start ? UiValue.Of(start) : UiValue.None<string>(),
			EndColor = face.Color is { } end ? UiValue.Of(end) : UiValue.None<string>()
		};
	}

	private static UiSize TextSize(double basis, double? maxOfCross, double cap)
	{
		return UiSize.Of(new UiLength { Basis = basis, MaxOfCross = maxOfCross, MaxOfCell = cap / UiLength.Cell });
	}
}
