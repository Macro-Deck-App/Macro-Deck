using MacroDeckHost.Application.Ui.Transport.Messages.Weather;

namespace MacroDeckHost.Widgets.Weather;

internal static class WeatherPreviewSamples
{
	// Both samples carry the same number of forecast days and hours on purpose: the scenarios exist to
	// compare one condition's rendering against another's, which a different amount of data would defeat.

	public static WeatherStatePayload ClearDay() => new()
	{
		IsAvailable = true,
		LocationName = "Home",
		Temperature = 21.5,
		ApparentTemperature = 20.8,
		Condition = "clear",
		IsDay = true,
		Unit = "celsius",
		Days =
		[
			new WeatherForecastDayPayload { Date = "2026-08-26", Condition = "clear", Min = 14, Max = 23 },
			new WeatherForecastDayPayload { Date = "2026-08-27", Condition = "partly-cloudy", Min = 13, Max = 21 },
			new WeatherForecastDayPayload { Date = "2026-08-28", Condition = "rain", Min = 12, Max = 18 },
		],
		Hours =
		[
			new WeatherHourPayload { Time = "12:00", Condition = "clear", Temperature = 21.5 },
			new WeatherHourPayload { Time = "13:00", Condition = "clear", Temperature = 22.1 },
			new WeatherHourPayload { Time = "14:00", Condition = "partly-cloudy", Temperature = 21.8 },
		],
		WindSpeed = 12,
		WindDirection = 220,
		Humidity = 48,
		Precipitation = 0,
		Sunrise = "06:12",
		Sunset = "20:47",
	};

	public static WeatherStatePayload Rain() => new()
	{
		IsAvailable = true,
		LocationName = "Home",
		Temperature = 14.2,
		ApparentTemperature = 12.9,
		Condition = "rain",
		IsDay = true,
		Unit = "celsius",
		Days =
		[
			new WeatherForecastDayPayload { Date = "2026-08-26", Condition = "rain", Min = 11, Max = 16 },
			new WeatherForecastDayPayload { Date = "2026-08-27", Condition = "rain-showers", Min = 10, Max = 15 },
			new WeatherForecastDayPayload { Date = "2026-08-28", Condition = "partly-cloudy", Min = 12, Max = 19 },
		],
		Hours =
		[
			new WeatherHourPayload
			{
				Time = "12:00", Condition = "rain", Temperature = 14.2, PrecipitationProbability = 0.8,
			},
			new WeatherHourPayload
			{
				Time = "13:00", Condition = "rain", Temperature = 13.9, PrecipitationProbability = 0.9,
			},
			new WeatherHourPayload
			{
				Time = "14:00", Condition = "rain-showers", Temperature = 14.4, PrecipitationProbability = 0.6,
			},
		],
		WindSpeed = 24,
		WindDirection = 180,
		Humidity = 88,
		Precipitation = 4.2,
		Sunrise = "06:12",
		Sunset = "20:47",
	};
}
