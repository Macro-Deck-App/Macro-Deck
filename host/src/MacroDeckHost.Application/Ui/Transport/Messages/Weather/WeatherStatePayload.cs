using System.Globalization;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Weather;

public class WeatherForecastDayPayload
{
	public string Date { get; set; } = string.Empty;

	public string Condition { get; set; } = "unknown";

	public double Min { get; set; }

	public double Max { get; set; }
}

public class WeatherHourPayload
{
	/// <summary>The hour in the location's own clock, as <c>HH:mm</c>. Formatted here rather than sent as
	/// an instant: the reader must not shift it into its own zone, which is what any date type on the wire
	/// would invite.</summary>
	public string Time { get; set; } = string.Empty;

	public string Condition { get; set; } = "unknown";

	public double Temperature { get; set; }

	public double? PrecipitationProbability { get; set; }
}

public class WeatherStatePayload
{
	public string? InstanceId { get; set; }

	public bool IsAvailable { get; set; }

	public bool StationExists { get; set; } = true;

	public string LocationName { get; set; } = string.Empty;

	public double? Temperature { get; set; }

	public double? ApparentTemperature { get; set; }

	public string Condition { get; set; } = "unknown";

	public bool IsDay { get; set; }

	public string Unit { get; set; } = "celsius";

	public List<WeatherForecastDayPayload> Days { get; set; } = new();

	public List<WeatherHourPayload> Hours { get; set; } = new();

	public double? WindSpeed { get; set; }

	public double? WindDirection { get; set; }

	public double? Humidity { get; set; }

	public double? Precipitation { get; set; }

	/// <summary>Local <c>HH:mm</c>, for the reason <see cref="WeatherHourPayload.Time" /> gives.</summary>
	public string? Sunrise { get; set; }

	/// <summary>Local <c>HH:mm</c>, for the reason <see cref="WeatherHourPayload.Time" /> gives.</summary>
	public string? Sunset { get; set; }

	public static WeatherStatePayload From(WeatherSnapshot snapshot, string? instanceId = null)
		=> new()
		{
			InstanceId = instanceId,
			IsAvailable = snapshot.IsAvailable,
			LocationName = snapshot.LocationName,
			Temperature = snapshot.Temperature,
			ApparentTemperature = snapshot.ApparentTemperature,
			Condition = ToSlug(snapshot.Condition),
			IsDay = snapshot.IsDay,
			Unit = snapshot.Unit.ToString().ToLowerInvariant(),
			Days = snapshot.Days
				.Select(d => new WeatherForecastDayPayload
				{
					Date = d.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
					Condition = ToSlug(d.Condition),
					Min = d.Min,
					Max = d.Max
				})
				.ToList(),
			Hours = snapshot.Hours
				.Select(h => new WeatherHourPayload
				{
					Time = h.Time.ToString("HH:mm", CultureInfo.InvariantCulture),
					Condition = ToSlug(h.Condition),
					Temperature = h.Temperature,
					PrecipitationProbability = h.PrecipitationProbability
				})
				.ToList(),
			WindSpeed = snapshot.WindSpeed,
			WindDirection = snapshot.WindDirection,
			Humidity = snapshot.Humidity,
			Precipitation = snapshot.Precipitation,
			Sunrise = snapshot.Sunrise?.ToString("HH:mm", CultureInfo.InvariantCulture),
			Sunset = snapshot.Sunset?.ToString("HH:mm", CultureInfo.InvariantCulture)
		};

	public static WeatherStatePayload Unavailable(string? instanceId = null, string locationName = "")
		=> From(WeatherSnapshot.Unavailable(locationName), instanceId);

	public static WeatherStatePayload UnknownStation(string? instanceId)
	{
		var payload = Unavailable(instanceId);
		payload.StationExists = false;
		return payload;
	}

	private static string ToSlug(WeatherCondition condition) => condition switch
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
