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

/// <summary>
/// The Weather integration's detail dialog: everything a glanceable widget has no room for - the hourly
/// strip, wind, humidity, precipitation, sunrise and sunset, and the full multi-day forecast.
/// </summary>
internal static class WeatherDetailsView
{
	// A dialog is wide, and the widget profile sizes every length off the box's *smaller* side. Lengths
	// here are therefore chosen against the dialog's height, not its width.
	private const int HourlyColumns = 8;

	private const int ForecastRows = 7;

	public static UiElement Build(UiAsyncState<WeatherStatePayload> state, WeatherIconResources icons)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(icons);

		return new UiStack
		{
			Key = "weather-details",
			Direction = UiComponentDirections.Vertical,
			Padding = 0.04,
			Gap = 0.03,
			Children =
			[
				new UiWhen
				{
					Key = "unknownStationGate",
					Condition = () => !state.Value.StationExists,
					Content = () => Message(AppStrings.Integrations.Weather.Details.NoStation()),
				},
				new UiWhen
				{
					Key = "unavailableGate",
					Condition = () => state.Value.StationExists && !state.Value.IsAvailable,
					Content = () => Message(AppStrings.Integrations.Weather.Details.Unavailable()),
				},
				new UiWhen
				{
					Key = "availableGate",
					Condition = () => state.Value.IsAvailable,
					Content = () => Available(state, icons),
				},
			],
		};
	}

	private static UiStack Message(LocalizedText text)
		=> new()
		{
			Key = "message",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Fill = true,
			Children =
				[new UiTextRun { Key = "text", Text = text, Size = 0.06, Role = UiComponentTextRoles.Secondary }],
		};

	/// <summary>
	/// A list rather than a stack: this is the one part of the dialog that can hold more than the box it
	/// is given - an eight-day forecast, or a station that reports more hours than another - and a list
	/// is how the profile expresses that. Neither client scrolls a dialog on the tree's behalf, by
	/// design: a second scroll container around a tree that already scrolls puts two bars on one dialog.
	/// Left as a stack, the days below the fold were simply cut off.
	/// </summary>
	private static UiList Available(UiAsyncState<WeatherStatePayload> state, WeatherIconResources icons)
		=> new()
		{
			Key = "available",
			Gap = UiSize.FromBasis(0.035),
			Fill = true,
			Children =
			[
				Current(state, icons),
				Measurements(state),
				new UiWhen
				{
					Key = "hourlyGate",
					Condition = () => state.Value.Hours.Count > 0,
					Content = () => Hourly(state, icons),
				},
				new UiWhen
				{
					Key = "forecastGate",
					Condition = () => state.Value.Days.Count > 0,
					Content = () => Forecast(state, icons),
				},
			],
		};

	private static UiStack Current(UiAsyncState<WeatherStatePayload> state, WeatherIconResources icons)
		=> new()
		{
			Key = "current",
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = 0.03,
			Children =
			[
				new UiImage
				{
					Key = "icon",
					Source = UiValue.From(() => Icon(icons, state.Value.Condition, state.Value.IsDay)),
					Size = 0.16,
				},
				new UiStack
				{
					Key = "readings",
					Direction = UiComponentDirections.Vertical,
					Fill = true,
					Children =
					[
						new UiTextRun
						{
							Key = "location",
							Text = UiText.From(() => state.Value.LocationName),
							Size = 0.05,
							Role = UiComponentTextRoles.Secondary,
						},
						new UiTextRun
						{
							Key = "temperature",
							Text = UiText.From(() => Temperature(state.Value.Temperature, state.Value.Unit)),
							Size = 0.12,
							Weight = UiComponentTextWeights.SemiBold,
						},
						new UiTextRun
						{
							Key = "apparent",
							Text = UiText.FromLocalized(() => AppStrings.Integrations.Weather.Details.FeelsLike(
								Temperature(state.Value.ApparentTemperature, state.Value.Unit))),
							Size = 0.045,
							Role = UiComponentTextRoles.Muted,
						},
					],
				},
			],
		};

	private static UiStack Measurements(UiAsyncState<WeatherStatePayload> state)
		=> new()
		{
			Key = "measurements",
			Direction = UiComponentDirections.Horizontal,
			Justify = UiComponentJustify.SpaceBetween,
			Gap = 0.02,
			Children =
			[
				Measurement("wind",
					AppStrings.Integrations.Weather.Details.Wind(),
					() => Wind(state.Value)),
				Measurement("humidity",
					AppStrings.Integrations.Weather.Details.Humidity(),
					() => state.Value.Humidity is { } humidity
						? AppStrings.Integrations.Weather.Details.HumidityValue(Number(humidity, 0))
						: Placeholder()),
				Measurement("precipitation",
					AppStrings.Integrations.Weather.Details.Precipitation(),
					() => Precipitation(state.Value)),
				Measurement("sunrise",
					AppStrings.Integrations.Weather.Details.Sunrise(),
					() => LocalizedText.FromLiteral(state.Value.Sunrise ?? "-")),
				Measurement("sunset",
					AppStrings.Integrations.Weather.Details.Sunset(),
					() => LocalizedText.FromLiteral(state.Value.Sunset ?? "-")),
			],
		};

	private static UiStack Measurement(string key, LocalizedText label, Func<LocalizedText> value)
		=> new()
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Align = UiComponentAlignments.Center,
			Fill = true,
			Children =
			[
				new UiTextRun
				{
					Key = "label", Text = label, Size = 0.035, Role = UiComponentTextRoles.Muted,
				},
				new UiTextRun
				{
					Key = "value", Text = UiText.Optional(() => value()), Size = 0.05,
				},
			],
		};

	private static UiStack Hourly(UiAsyncState<WeatherStatePayload> state, WeatherIconResources icons)
		=> new()
		{
			Key = "hourly",
			Direction = UiComponentDirections.Vertical,
			Gap = 0.015,
			Children =
			[
				new UiTextRun
				{
					Key = "heading",
					Text = AppStrings.Integrations.Weather.Details.Hourly(),
					Size = 0.04,
					Role = UiComponentTextRoles.Secondary,
					Weight = UiComponentTextWeights.SemiBold,
				},
				new UiStack
				{
					Key = "hours",
					Direction = UiComponentDirections.Horizontal,
					Justify = UiComponentJustify.SpaceBetween,
					Gap = 0.015,
					Children =
					[
						new UiRepeat<WeatherHourPayload>
						{
							Key = "hourRepeat",
							Items = UiValue.From<IReadOnlyList<WeatherHourPayload>>(() =>
								Take(state.Value.Hours, HourlyColumns)),
							// A repeat key composes into a node id, whose grammar has no colon - the payload's
							// own HH:mm would make the whole tree unbuildable rather than just this row.
							KeySelector = hour => hour.Time.Replace(':', '-'),
							Template = (hour, key) => Hour(hour, key, state, icons),
						},
					],
				},
			],
		};

	private static UiStack Hour(
		WeatherHourPayload hour,
		string key,
		UiAsyncState<WeatherStatePayload> state,
		WeatherIconResources icons)
		=> new()
		{
			Key = key,
			Direction = UiComponentDirections.Vertical,
			Align = UiComponentAlignments.Center,
			Gap = 0.008,
			Fill = true,
			Children =
			[
				// A text fills its column across the stack, so centring the column only moves the narrower
				// icon: without their own alignment these two sit left while the icon sits centred.
				new UiTextRun
				{
					Key = "time",
					Text = hour.Time,
					Size = 0.032,
					Align = UiComponentAlignments.Center,
					Role = UiComponentTextRoles.Muted,
				},
				// Hours are the only place the day/night variant cannot be read off the snapshot: an hour
				// twelve hours out is not the current daylight. The still icon avoids implying otherwise.
				new UiImage
				{
					Key = "icon", Source = icons.Still[WeatherWidgetIcons.IconName(hour.Condition, true)], Size = 0.05,
				},
				new UiTextRun
				{
					Key = "temperature",
					Text = UiText.From(() => Temperature(hour.Temperature, state.Value.Unit)),
					Size = 0.036,
					Align = UiComponentAlignments.Center,
				},
			],
		};

	private static UiStack Forecast(UiAsyncState<WeatherStatePayload> state, WeatherIconResources icons)
		=> new()
		{
			Key = "forecast",
			Direction = UiComponentDirections.Vertical,
			Gap = 0.01,
			Children =
			[
				new UiTextRun
				{
					Key = "heading",
					Text = AppStrings.Integrations.Weather.Details.Forecast(),
					Size = 0.04,
					Role = UiComponentTextRoles.Secondary,
					Weight = UiComponentTextWeights.SemiBold,
				},
				new UiRepeat<ForecastRow>
				{
					Key = "dayRepeat",
					Items = UiValue.From<IReadOnlyList<ForecastRow>>(() => Rows(state.Value.Days, state.Value.Unit)),
					KeySelector = row => row.Key,
					Template = (row, key) => Day(row, key, state, icons),
				},
			],
		};

	/// <summary>
	/// The widget's forecast row, at the dialog's own scale: the same span bar, the same colour ramp and
	/// the same today marker, so the two readings of one week agree rather than merely coexist. The
	/// geometry comes from <see cref="WeatherWidgetView" /> rather than being restated here.
	/// </summary>
	private static UiStack Day(
		ForecastRow row,
		string key,
		UiAsyncState<WeatherStatePayload> state,
		WeatherIconResources icons)
	{
		var day = row.Day;
		var (start, end) = WeatherWidgetView.RangeBarSpan(day.Min, day.Max, row.WeekMin, row.Span);

		return new UiStack
		{
			Key = key,
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
					Gap = 0.015,
					MainSize = 0.155,
					Children =
					[
						new UiTextRun { Key = "date", Text = DayLabel(day.Date), MainSize = 0.095, Size = 0.036 },
						new UiImage
						{
							Key = "icon",
							Source = icons.Still[WeatherWidgetIcons.IconName(day.Condition, true)],
							MainSize = 0.045,
							Size = 0.045,
						},
					],
				},
				// Every text in this row declares its own main size: a text measures zero along a
				// horizontal stack, so the filling bar would otherwise be handed their width as well.
				new UiTextRun
				{
					Key = "min",
					Text = UiText.From(() => Temperature(day.Min, state.Value.Unit)),
					MainSize = 0.12,
					Size = 0.036,
					Align = UiComponentAlignments.End,
					Role = UiComponentTextRoles.Muted,
				},
				new UiRangeBar
				{
					Key = "bar",
					Fill = true,
					Thickness = 0.022,
					Start = start,
					End = end,
					StartColor = WeatherWidgetView.TempColorHex(day.Min, row.Unit),
					EndColor = WeatherWidgetView.TempColorHex(day.Max, row.Unit),
					// Only today's row carries the marker - it reads as "where the current temperature
					// falls in today's range", which says nothing about a day that has not started.
					Marker = row.Index == 0
						? UiValue.Optional<double>(() =>
							WeatherWidgetView.MarkerFraction(state.Value.Temperature, row.WeekMin, row.Span) is { } at
								? UiValue.Of(at)
								: UiValue.None<double>())
						: UiValue.None<double>(),
				},
				new UiTextRun
				{
					Key = "max",
					Text = UiText.From(() => Temperature(day.Max, state.Value.Unit)),
					MainSize = 0.12,
					Size = 0.036,
					Align = UiComponentAlignments.End,
				},
			],
		};
	}

	/// <summary>
	/// The rows the forecast draws, each carrying the week's own span so every bar is read against the
	/// same scale rather than against its own day.
	/// </summary>
	private static List<ForecastRow> Rows(List<WeatherForecastDayPayload> days, string unit)
	{
		var take = Math.Min(ForecastRows, days.Count);
		if (take == 0)
		{
			return [];
		}

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
		var used = new HashSet<string>(StringComparer.Ordinal);
		for (var i = 0; i < take; i++)
		{
			// A repeat key composes into a node id, and a date the provider gave us is not guaranteed to
			// be one - an invalid key would fail the whole tree, not just its row.
			var key = !string.IsNullOrEmpty(days[i].Date) && UiIdentifier.IsValid(days[i].Date)
				? days[i].Date
				: $"day{i}";
			if (!used.Add(key))
			{
				key = $"{key}-{i}";
				used.Add(key);
			}

			rows.Add(new ForecastRow(days[i], key, i, weekMin, span, unit));
		}

		return rows;
	}

	private sealed record ForecastRow(
		WeatherForecastDayPayload Day,
		string Key,
		int Index,
		double WeekMin,
		double Span,
		string Unit);

	private static LocalizedText Wind(WeatherStatePayload payload)
	{
		if (payload.WindSpeed is not { } speed)
		{
			return Placeholder();
		}

		return IsFahrenheit(payload.Unit)
			? AppStrings.Integrations.Weather.Details.WindImperial(Number(speed, 0))
			: AppStrings.Integrations.Weather.Details.WindMetric(Number(speed, 0));
	}

	private static LocalizedText Precipitation(WeatherStatePayload payload)
	{
		if (payload.Precipitation is not { } precipitation)
		{
			return Placeholder();
		}

		return IsFahrenheit(payload.Unit)
			? AppStrings.Integrations.Weather.Details.PrecipitationImperial(Number(precipitation, 2))
			: AppStrings.Integrations.Weather.Details.PrecipitationMetric(Number(precipitation, 1));
	}

	private static UiResource Icon(WeatherIconResources icons, string condition, bool isDay)
		=> icons.Animated[WeatherWidgetIcons.IconName(condition, isDay)];

	private static bool IsFahrenheit(string unit)
		=> string.Equals(unit, "fahrenheit", StringComparison.OrdinalIgnoreCase);

	private static LocalizedText Placeholder() => LocalizedText.FromLiteral("-");

	private static string Number(double value, int decimals)
		=> value.ToString($"F{decimals}", CultureInfo.CurrentCulture);

	private static string Temperature(double? value, string unit)
		=> value is null ? "-" : $"{Number(value.Value, 0)}{(IsFahrenheit(unit) ? "°F" : "°C")}";

	private static string DayLabel(string isoDate)
		=> DateOnly.TryParse(isoDate, CultureInfo.InvariantCulture, out var date)
			? date.ToString("ddd", CultureInfo.CurrentCulture)
			: isoDate;

	private static List<T> Take<T>(List<T> items, int count)
		=> items.Count <= count ? items : items.GetRange(0, count);
}
