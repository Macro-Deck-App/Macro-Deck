using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Integrations.Weather;

internal interface IOpenMeteoClient
{
	Task<OpenMeteoForecastResponse?> GetForecastAsync(
		double latitude,
		double longitude,
		TemperatureUnit unit,
		CancellationToken cancellationToken);

	Task<IReadOnlyList<GeocodingResult>> SearchLocationAsync(string query, CancellationToken cancellationToken);
}
