using System.Text.Json.Serialization;

namespace MacroDeckHost.Integrations.Weather;

internal sealed class OpenMeteoForecastResponse
{
	[JsonPropertyName("current")]
	public OpenMeteoCurrent? Current { get; set; }

	[JsonPropertyName("daily")]
	public OpenMeteoDaily? Daily { get; set; }

	[JsonPropertyName("hourly")]
	public OpenMeteoHourly? Hourly { get; set; }
}

internal sealed class OpenMeteoCurrent
{
	[JsonPropertyName("temperature_2m")]
	public double? Temperature { get; set; }

	[JsonPropertyName("apparent_temperature")]
	public double? ApparentTemperature { get; set; }

	[JsonPropertyName("weather_code")]
	public int WeatherCode { get; set; }

	[JsonPropertyName("is_day")]
	public int IsDay { get; set; }

	[JsonPropertyName("wind_speed_10m")]
	public double? WindSpeed { get; set; }

	[JsonPropertyName("wind_direction_10m")]
	public double? WindDirection { get; set; }

	[JsonPropertyName("relative_humidity_2m")]
	public double? Humidity { get; set; }

	[JsonPropertyName("precipitation")]
	public double? Precipitation { get; set; }
}

internal sealed class OpenMeteoHourly
{
	[JsonPropertyName("time")]
	public List<string> Time { get; set; } = new();

	[JsonPropertyName("temperature_2m")]
	public List<double> Temperature { get; set; } = new();

	[JsonPropertyName("weather_code")]
	public List<int> WeatherCode { get; set; } = new();

	[JsonPropertyName("precipitation_probability")]
	public List<double?> PrecipitationProbability { get; set; } = new();
}

internal sealed class OpenMeteoDaily
{
	[JsonPropertyName("time")]
	public List<string> Time { get; set; } = new();

	[JsonPropertyName("temperature_2m_max")]
	public List<double> TemperatureMax { get; set; } = new();

	[JsonPropertyName("temperature_2m_min")]
	public List<double> TemperatureMin { get; set; } = new();

	[JsonPropertyName("weather_code")]
	public List<int> WeatherCode { get; set; } = new();

	[JsonPropertyName("sunrise")]
	public List<string> Sunrise { get; set; } = new();

	[JsonPropertyName("sunset")]
	public List<string> Sunset { get; set; } = new();
}

internal sealed class GeocodingResponse
{
	[JsonPropertyName("results")]
	public List<GeocodingResult>? Results { get; set; }
}

internal sealed class GeocodingResult
{
	[JsonPropertyName("name")]
	public string Name { get; set; } = string.Empty;

	[JsonPropertyName("latitude")]
	public double Latitude { get; set; }

	[JsonPropertyName("longitude")]
	public double Longitude { get; set; }

	[JsonPropertyName("country")]
	public string? Country { get; set; }

	[JsonPropertyName("admin1")]
	public string? Admin1 { get; set; }
}
