using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Identity;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.Weather;

internal static class WeatherWidgetView
{
	public static UiElement Build(
		UiAsyncState<WeatherStatePayload> state,
		WeatherWidgetData config,
		WeatherIconResources icons,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(icons);

		return new UiStack
		{
			Key = "weather",
			Direction = UiComponentDirections.Vertical,
			Padding = WidgetSafeArea.For(cornerRadius),
			Children =
			[
				new UiWhen
				{
					Key = "availableGate",
					Condition = () => state.Value.IsAvailable,
					Content = () => BuildCurrent(state, config, icons),
				},
				new UiWhen
				{
					Key = "forecastGate",
					Condition = () => HasForecastRows(state.Value, config),
					Content = () => BuildForecastStack(state, config, icons),
				},
				new UiWhen
				{
					Key = "unavailableGate",
					Condition = () => !state.Value.IsAvailable,
					Content = () => BuildUnavailable(state),
				},
			],
		};
	}

	private static bool HasForecastRows(WeatherStatePayload payload, WeatherWidgetData config)
		=> config.ShowForecast && payload.Days.Count > 0;

	private static UiStack BuildCurrent(
		UiAsyncState<WeatherStatePayload> state,
		WeatherWidgetData config,
		WeatherIconResources icons)
	{
		var justify = config is { ShowIcon: true, ShowTemperature: true } ? UiComponentJustify.SpaceBetween
			: config.ShowTemperature ? UiComponentJustify.End
			: UiComponentJustify.Start;

		return new UiStack
		{
			Key = "current",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Gap = 0.02,
			MainSize = UiSize.Optional(()
				=> HasForecastRows(state.Value, config) ? UiSize.FromBasis(0.42) : UiSize.None()),
			Fill = UiValue.Optional(()
				=> HasForecastRows(state.Value, config) ? UiValue.None<bool>() : UiValue.Of(true)),
			Children =
			[
				new UiStack
				{
					Key = "head",
					Direction = UiComponentDirections.Horizontal,
					Align = UiComponentAlignments.Center,
					Justify = justify,
					Children =
					[
						new UiWhen
						{
							Key = "iconGate",
							Condition = () => config.ShowIcon,
							Content = () => new UiImage
							{
								Key = "icon",
								Size = 0.2,
								Source = UiValue.Optional(()
									=> ResolveIcon(config.AnimateIcon ? icons.Animated : icons.Still,
										state.Value.Condition,
										state.Value.IsDay)),
							},
						},
						new UiWhen
						{
							Key = "tempGate",
							Condition = () => config.ShowTemperature,
							Content = () => new UiTextRun
							{
								Key = "temp",
								Size = 0.17,
								Weight = UiComponentTextWeights.Bold,
								Role = UiComponentTextRoles.Primary,
								Align = UiComponentAlignments.End,
								Text = UiText.FromLocalized(() => TemperatureText(state.Value.Temperature)),
							},
						},
					],
				},
				new UiWhen
				{
					Key = "labelsGate",
					Condition = () => config.ShowCondition,
					Content = () => new UiStack
					{
						Key = "labels",
						Direction = UiComponentDirections.Vertical,
						Gap = 0.008,
						Children =
						[
							new UiTextRun
							{
								Key = "condition",
								Size = 0.11,
								MinSize = 0.078,
								Weight = UiComponentTextWeights.SemiBold,
								Role = UiComponentTextRoles.Primary,
								Text = UiText.FromLocalized(() => ConditionText(state.Value.Condition)),
							},
							new UiWhen
							{
								Key = "locationGate",
								Condition = () => !string.IsNullOrEmpty(state.Value.LocationName),
								Content = () => new UiTextRun
								{
									Key = "location",
									Size = 0.062,
									MinSize = 0.05,
									Weight = UiComponentTextWeights.Medium,
									Role = UiComponentTextRoles.Secondary,
									Text = UiText.From(() => state.Value.LocationName),
								},
							},
						],
					},
				},
			],
		};
	}

	private static UiStack BuildForecastStack(
		UiAsyncState<WeatherStatePayload> state,
		WeatherWidgetData config,
		WeatherIconResources icons)
	{
		List<WeatherForecastDayPayload>? lastRawDays = null;
		string? lastUnit = null;
		IReadOnlyList<ForecastRow>? lastRows = null;

		return new UiStack
		{
			Key = "forecast",
			Fill = true,
			Direction = UiComponentDirections.Vertical,
			Children =
			[
				new UiRepeat<ForecastRow>
				{
					Key = "days",
					Items = UiValue.From(() =>
					{
						var payload = state.Value;

						if (!ReferenceEquals(payload.Days, lastRawDays) ||
							!string.Equals(payload.Unit, lastUnit, StringComparison.Ordinal))
						{
							lastRawDays = payload.Days;
							lastUnit = payload.Unit;
							lastRows = BuildRows(payload.Days, payload.Unit, config);
						}

						return lastRows!;
					}),
					KeySelector = row => row.ItemKey,
					Template = (row, _) => BuildForecastRow(row, state, icons),
				},
			],
		};
	}

	private static UiStack BuildForecastRow(
		ForecastRow row,
		UiAsyncState<WeatherStatePayload> state,
		WeatherIconResources icons)
	{
		var day = row.Day;
		var iconName = WeatherWidgetIcons.IconName(day.Condition, isDay: true);
		var (start, end) = RangeBarSpan(day.Min, day.Max, row.WeekMin, row.Span);

		return new UiStack
		{
			Key = row.ItemKey,
			Fill = true,
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = 0.02,
			Children =
			[
				new UiStack
				{
					Key = "label",
					Direction = UiComponentDirections.Horizontal,
					Align = UiComponentAlignments.Center,
					Children =
					[
						new UiTextRun
						{
							Key = "day",
							MainSize = UiSize.FromBasis(0.114, 0.95),
							Size = UiSize.FromBasis(0.06, 0.5),
							Weight = UiComponentTextWeights.Medium,
							Role = UiComponentTextRoles.Primary,
							Text = TryWeekdayText(day.Date, out var weekday) ? weekday : UiText.None(),
						},
						new UiImage
						{
							Key = "icon",
							MainSize = UiSize.FromBasis(0.09, 0.8),
							Size = UiSize.FromBasis(0.09, 0.8),
							Source = icons.Still.TryGetValue(iconName, out var rowIcon)
								? UiValue.Of(rowIcon)
								: UiValue.None<UiResource>(),
						},
					],
				},
				new UiTextRun
				{
					Key = "min",
					MainSize = UiSize.FromBasis(0.144, 1.2),
					Size = UiSize.FromBasis(0.06, 0.5),
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.End,
					Text = TemperatureText(day.Min),
				},
				new UiRangeBar
				{
					Key = "bar",
					Fill = true,
					Thickness = UiSize.FromBasis(0.03, 0.35),
					Start = start,
					End = end,
					StartColor = TempColorHex(day.Min, row.Unit),
					EndColor = TempColorHex(day.Max, row.Unit),
					Marker = row.Index == 0
						? UiValue.Optional(() =>
							AsMarkerValue(MarkerFraction(state.Value.Temperature, row.WeekMin, row.Span)))
						: UiValue.None<double>(),
				},
				new UiTextRun
				{
					Key = "max",
					MainSize = UiSize.FromBasis(0.144, 1.2),
					Size = UiSize.FromBasis(0.06, 0.5),
					Weight = UiComponentTextWeights.Medium,
					Role = UiComponentTextRoles.Primary,
					Align = UiComponentAlignments.End,
					Text = TemperatureText(day.Max),
				},
			],
		};
	}

	private static UiStack BuildUnavailable(UiAsyncState<WeatherStatePayload> state)
		=> new()
		{
			Key = "state",
			Fill = true,
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Gap = 0.02,
			Children =
			[
				new UiTextRun
				{
					Key = "title",
					Size = 0.09,
					Weight = UiComponentTextWeights.SemiBold,
					Role = UiComponentTextRoles.Secondary,
					Align = UiComponentAlignments.Center,
					Text = UiText.Optional(() => TitleText(state.Value)),
				},
				new UiTextRun
				{
					Key = "hint",
					Size = 0.06,
					Role = UiComponentTextRoles.Muted,
					Align = UiComponentAlignments.Center,
					MaxLines = 2,
					Text = UiText.FromLocalized(() => HintText(state.Value)),
				},
			],
		};

	private static UiText TitleText(WeatherStatePayload payload)
	{
		if (payload.StationExists)
		{
			return string.IsNullOrEmpty(payload.LocationName)
				? AppStrings.Widgets.Weather.CardTitle()
				: payload.LocationName;
		}

		return payload.InstanceId is null
			? AppStrings.Widgets.Weather.NoLocation()
			: AppStrings.Widgets.Weather.LocationUnavailable();
	}

	private static LocalizedString HintText(WeatherStatePayload payload)
	{
		if (payload.StationExists)
		{
			return AppStrings.Widgets.Weather.Loading();
		}

		return payload.InstanceId is null
			? AppStrings.Widgets.Weather.SetUpIntegrationHint()
			: AppStrings.Widgets.Weather.PickLocationHint();
	}

	private static UiValue<UiResource> ResolveIcon(IReadOnlyDictionary<string, UiResource> icons,
		string slug,
		bool isDay)
		=> icons.TryGetValue(WeatherWidgetIcons.IconName(slug, isDay), out var resource)
			? UiValue.Of(resource)
			: UiValue.None<UiResource>();

	private static UiValue<double> AsMarkerValue(double? marker)
		=> marker is { } value ? UiValue.Of(value) : UiValue.None<double>();

	internal static double? MarkerFraction(double? temperature, double weekMin, double span)
	{
		if (temperature is not { } value || double.IsNaN(value) || double.IsInfinity(value))
		{
			return null;
		}

		return Math.Clamp((value - weekMin) / span, 0, 1);
	}

	internal static (double Start, double End) RangeBarSpan(double min, double max, double weekMin, double span)
	{
		var start = (min - weekMin) / span;
		var width = Math.Max(0.08, (max - min) / span);
		var end = Math.Min(1.0, start + width);

		return (start, end);
	}

	private static LocalizedString TemperatureText(double? value)
		=> value is null || double.IsNaN(value.Value) || double.IsInfinity(value.Value)
			? AppStrings.Widgets.Weather.TemperatureUnknown()
			: AppStrings.Widgets.Weather.TemperatureValue((int)Math.Round(value.Value));

	private static LocalizedString ConditionText(string slug) => slug switch
	{
		"clear" => AppStrings.Widgets.Weather.Condition.Clear(),
		"mainly-clear" => AppStrings.Widgets.Weather.Condition.MainlyClear(),
		"partly-cloudy" => AppStrings.Widgets.Weather.Condition.PartlyCloudy(),
		"overcast" => AppStrings.Widgets.Weather.Condition.Overcast(),
		"fog" => AppStrings.Widgets.Weather.Condition.Fog(),
		"drizzle" => AppStrings.Widgets.Weather.Condition.Drizzle(),
		"rain" => AppStrings.Widgets.Weather.Condition.Rain(),
		"freezing-rain" => AppStrings.Widgets.Weather.Condition.FreezingRain(),
		"snow" => AppStrings.Widgets.Weather.Condition.Snow(),
		"snow-grains" => AppStrings.Widgets.Weather.Condition.SnowGrains(),
		"rain-showers" => AppStrings.Widgets.Weather.Condition.RainShowers(),
		"snow-showers" => AppStrings.Widgets.Weather.Condition.SnowShowers(),
		"thunderstorm" => AppStrings.Widgets.Weather.Condition.Thunderstorm(),
		_ => AppStrings.Widgets.Weather.Condition.Unknown(),
	};

	private static bool TryWeekdayText(string isoDate, out LocalizedString text)
	{
		if (DateTime.TryParseExact(isoDate,
			"yyyy-MM-dd",
			CultureInfo.InvariantCulture,
			DateTimeStyles.None,
			out var date))
		{
			text = date.DayOfWeek switch
			{
				DayOfWeek.Monday => AppStrings.Widgets.Weather.Weekday.Mon(),
				DayOfWeek.Tuesday => AppStrings.Widgets.Weather.Weekday.Tue(),
				DayOfWeek.Wednesday => AppStrings.Widgets.Weather.Weekday.Wed(),
				DayOfWeek.Thursday => AppStrings.Widgets.Weather.Weekday.Thu(),
				DayOfWeek.Friday => AppStrings.Widgets.Weather.Weekday.Fri(),
				DayOfWeek.Saturday => AppStrings.Widgets.Weather.Weekday.Sat(),
				_ => AppStrings.Widgets.Weather.Weekday.Sun(),
			};

			return true;
		}

		text = default;

		return false;
	}

	internal static string TempColorHex(double value, string unit)
	{
		var celsius = string.Equals(unit, "fahrenheit", StringComparison.OrdinalIgnoreCase)
			? (value - 32) * 5 / 9
			: value;

		var clamped = Math.Clamp(celsius, -10, 40);
		var t = (clamped + 10) / 50;
		var hue = 220 - t * 220;

		return HslToHex(hue, 0.85, 0.55);
	}

	private static string HslToHex(double hue, double saturation, double lightness)
	{
		var h = ((hue % 360) + 360) % 360;
		var chroma = (1 - Math.Abs(2 * lightness - 1)) * saturation;
		var x = chroma * (1 - Math.Abs(h / 60 % 2 - 1));
		var m = lightness - chroma / 2;

		var (r1, g1, b1) = h switch
		{
			< 60 => (chroma, x, 0.0),
			< 120 => (x, chroma, 0.0),
			< 180 => (0.0, chroma, x),
			< 240 => (0.0, x, chroma),
			< 300 => (x, 0.0, chroma),
			_ => (chroma, 0.0, x),
		};

		var r = (byte)Math.Round((r1 + m) * 255, MidpointRounding.AwayFromZero);
		var g = (byte)Math.Round((g1 + m) * 255, MidpointRounding.AwayFromZero);
		var b = (byte)Math.Round((b1 + m) * 255, MidpointRounding.AwayFromZero);

		return $"#{r:x2}{g:x2}{b:x2}";
	}

	private static List<ForecastRow> BuildRows(
		List<WeatherForecastDayPayload> days,
		string unit,
		WeatherWidgetData config)
	{
		if (!config.ShowForecast || days.Count == 0)
		{
			return [];
		}

		var take = Math.Min(config.ForecastDays, days.Count);
		var weekMin = double.PositiveInfinity;
		var weekMax = double.NegativeInfinity;

		for (var i = 0; i < take; i++)
		{
			weekMin = Math.Min(weekMin, days[i].Min);
			weekMax = Math.Max(weekMax, days[i].Max);
		}

		var span = weekMax - weekMin;

		if (span <= 0)
		{
			span = 1;
		}

		var rows = new List<ForecastRow>(take);
		var usedKeys = new HashSet<string>(StringComparer.Ordinal);

		for (var i = 0; i < take; i++)
		{
			var day = days[i];
			var key = !string.IsNullOrEmpty(day.Date) && UiIdentifier.IsValid(day.Date) ? day.Date : $"day{i}";

			if (!usedKeys.Add(key))
			{
				key = $"{key}-{i}";
				usedKeys.Add(key);
			}

			rows.Add(new ForecastRow(day, key, i, weekMin, span, unit));
		}

		return rows;
	}

	private sealed record ForecastRow(
		WeatherForecastDayPayload Day,
		string ItemKey,
		int Index,
		double WeekMin,
		double Span,
		string Unit);
}
