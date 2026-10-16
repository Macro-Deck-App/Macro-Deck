using System.Globalization;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Integrations.Weather.Widgets;

internal sealed class WeatherDetailsDay
{
	public string Date { get; set; } = string.Empty;

	public string Condition { get; set; } = "unknown";

	public double Min { get; set; }

	public double Max { get; set; }
}

internal sealed class WeatherDetailsHour
{
	public string Time { get; set; } = string.Empty;

	public string Condition { get; set; } = "unknown";

	public double Temperature { get; set; }

	public double? PrecipitationProbability { get; set; }
}

internal sealed class WeatherDetailsState
{
	public bool IsAvailable { get; set; }

	public bool StationExists { get; set; } = true;

	public string LocationName { get; set; } = string.Empty;

	public double? Temperature { get; set; }

	public double? ApparentTemperature { get; set; }

	public string Condition { get; set; } = "unknown";

	public bool IsDay { get; set; }

	public string Unit { get; set; } = "celsius";

	public List<WeatherDetailsDay> Days { get; set; } = [];

	public List<WeatherDetailsHour> Hours { get; set; } = [];

	public double? WindSpeed { get; set; }

	public double? WindDirection { get; set; }

	public double? Humidity { get; set; }

	public double? Precipitation { get; set; }

	public string? Sunrise { get; set; }

	public string? Sunset { get; set; }

	// Times stay in the location's own clock as HH:mm: shifting them into the reader's zone would be wrong.
	public static WeatherDetailsState From(WeatherSnapshot snapshot)
	{
		return new WeatherDetailsState
		{
			IsAvailable = snapshot.IsAvailable,
			LocationName = snapshot.LocationName,
			Temperature = snapshot.Temperature,
			ApparentTemperature = snapshot.ApparentTemperature,
			Condition = ToSlug(snapshot.Condition),
			IsDay = snapshot.IsDay,
			Unit = snapshot.Unit.ToString().ToLowerInvariant(),
			Days =
			[
				.. snapshot.Days.Select(day => new WeatherDetailsDay
				{
					Date = day.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
					Condition = ToSlug(day.Condition),
					Min = day.Min,
					Max = day.Max
				})
			],
			Hours =
			[
				.. snapshot.Hours.Select(hour => new WeatherDetailsHour
				{
					Time = hour.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
					Condition = ToSlug(hour.Condition),
					Temperature = hour.Temperature,
					PrecipitationProbability = hour.PrecipitationProbability
				})
			],
			WindSpeed = snapshot.WindSpeed,
			WindDirection = snapshot.WindDirection,
			Humidity = snapshot.Humidity,
			Precipitation = snapshot.Precipitation,
			Sunrise = snapshot.Sunrise?.ToString("HH:mm", CultureInfo.InvariantCulture),
			Sunset = snapshot.Sunset?.ToString("HH:mm", CultureInfo.InvariantCulture)
		};
	}

	public static WeatherDetailsState Unavailable(string locationName = "")
	{
		return From(WeatherSnapshot.Unavailable(locationName));
	}

	public static WeatherDetailsState UnknownStation()
	{
		var state = Unavailable();
		state.StationExists = false;
		return state;
	}

	private static string ToSlug(WeatherCondition condition)
	{
		return condition switch
		{
			WeatherCondition.Clear => "clear",
			WeatherCondition.MainlyClear => "mainly-clear",
			WeatherCondition.PartlyCloudy => "partly-cloudy",
			WeatherCondition.Overcast => "overcast",
			WeatherCondition.Fog => "fog",
			WeatherCondition.Drizzle => "drizzle",
			WeatherCondition.Rain => "rain",
			WeatherCondition.FreezingRain => "freezing-rain",
			WeatherCondition.Snow => "snow",
			WeatherCondition.SnowGrains => "snow-grains",
			WeatherCondition.RainShowers => "rain-showers",
			WeatherCondition.SnowShowers => "snow-showers",
			WeatherCondition.Thunderstorm => "thunderstorm",
			_ => "unknown"
		};
	}
}
