using System.Globalization;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Preview;

namespace MacroDeckHost.Widgets.Weather;

/// <summary>The card the widget picker draws for Weather: a mild, partly cloudy afternoon with a week
/// ahead of it. Fixed rather than read from a station, because a user choosing a widget type has not set
/// one up yet.</summary>
internal static class WeatherWidgetSample
{
	private static readonly (string Condition, double Min, double Max)[] _days =
	[
		("partly-cloudy", 12, 21),
		("clear", 13, 24),
		("rainy-1", 11, 18),
		("cloudy", 10, 17),
		("mainly-clear", 12, 22),
		("clear", 14, 25),
		("cloudy", 13, 20),
	];

	internal static async ValueTask<WeatherStatePayload> BuildAsync(WeatherWidgetData config,
		IWidgetSampleTextResolver text)
	{
		var location = await text.ResolveAsync(AppStrings.Widgets.SamplePreview.WeatherLocation())
			.ConfigureAwait(false);

		return new WeatherStatePayload
		{
			IsAvailable = true,
			StationExists = true,
			LocationName = location,
			Temperature = 21,
			ApparentTemperature = 20,
			Condition = "partly-cloudy",
			IsDay = true,
			Unit = "celsius",
			WindSpeed = 11,
			WindDirection = 220,
			Humidity = 58,
			Precipitation = 0,
			Sunrise = "06:12",
			Sunset = "20:34",
			Days = BuildDays(config.ForecastDays),
			Hours = BuildHours(),
		};
	}

	private static List<WeatherForecastDayPayload> BuildDays(int count)
	{
		// A fixed starting date, not today's: the sample must read the same whenever it is opened, and
		// nothing in the card shows the year.
		var start = new DateOnly(2024, 6, 3);

		return Enumerable.Range(0, Math.Clamp(count, 1, _days.Length))
			.Select(offset => new WeatherForecastDayPayload
			{
				Date = start.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
				Condition = _days[offset].Condition,
				Min = _days[offset].Min,
				Max = _days[offset].Max,
			})
			.ToList();
	}

	private static List<WeatherHourPayload> BuildHours()
		=> Enumerable.Range(0, 8)
			.Select(offset => new WeatherHourPayload
			{
				Time = $"{12 + offset:00}:00",
				Condition = offset < 4 ? "partly-cloudy" : "clear",
				Temperature = 21 + (offset < 4 ? offset : 6 - offset),
				PrecipitationProbability = offset < 4 ? 20 : 5,
			})
			.ToList();
}
