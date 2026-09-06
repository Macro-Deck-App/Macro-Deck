using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Integrations.Weather;

internal sealed class OpenMeteoClient : IOpenMeteoClient
{
	private const string ForecastUrl = "https://api.open-meteo.com/v1/forecast";
	private const string GeocodingUrl = "https://geocoding-api.open-meteo.com/v1/search";

	private const int ForecastDays = 7;

	// A day's worth: enough for the modal's hourly strip without asking the API for a week of hours.
	private const int ForecastHours = 24;

	private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web);

	private static readonly HttpClient _http = CreateHttpClient();

	public async Task<OpenMeteoForecastResponse?> GetForecastAsync(
		double latitude,
		double longitude,
		TemperatureUnit unit,
		CancellationToken cancellationToken)
	{
		var lat = latitude.ToString(CultureInfo.InvariantCulture);
		var lon = longitude.ToString(CultureInfo.InvariantCulture);
		var fahrenheit = unit == TemperatureUnit.Fahrenheit;
		var temperatureUnit = fahrenheit ? "fahrenheit" : "celsius";

		// Wind and precipitation follow the temperature unit rather than being asked for separately: a
		// reading in Fahrenheit alongside km/h and millimetres belongs to no measurement system anyone
		// uses, and WeatherSnapshot documents exactly this pairing.
		var windUnit = fahrenheit ? "mph" : "kmh";
		var precipitationUnit = fahrenheit ? "inch" : "mm";

		var url = $"{ForecastUrl}?latitude={lat}&longitude={lon}" +
			"&current=temperature_2m,apparent_temperature,weather_code,is_day," +
			"wind_speed_10m,wind_direction_10m,relative_humidity_2m,precipitation" +
			"&hourly=temperature_2m,weather_code,precipitation_probability" +
			$"&forecast_hours={ForecastHours}" +
			"&daily=temperature_2m_max,temperature_2m_min,weather_code,sunrise,sunset" +
			$"&timezone=auto&forecast_days={ForecastDays}&temperature_unit={temperatureUnit}" +
			$"&wind_speed_unit={windUnit}&precipitation_unit={precipitationUnit}";

		return await _http.GetFromJsonAsync<OpenMeteoForecastResponse>(url, _jsonOptions, cancellationToken);
	}

	public async Task<IReadOnlyList<GeocodingResult>> SearchLocationAsync(
		string query,
		CancellationToken cancellationToken)
	{
		var url = $"{GeocodingUrl}?name={Uri.EscapeDataString(query)}&count=5&language=en&format=json";

		var response = await _http.GetFromJsonAsync<GeocodingResponse>(url, _jsonOptions, cancellationToken);
		return response?.Results ?? [];
	}

	private static HttpClient CreateHttpClient()
	{
		var client = new HttpClient();
		client.DefaultRequestHeaders.UserAgent.ParseAdd("Macro-Deck-Weather/1.0");
		client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
		return client;
	}
}
