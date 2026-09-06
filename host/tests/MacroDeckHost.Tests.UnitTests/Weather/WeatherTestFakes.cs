using MacroDeckHost.Integrations.Weather;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Weather;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Weather;

internal sealed class FakeWeatherStation : IWeatherStation
{
	private readonly WeatherSnapshot _snapshot;

	public FakeWeatherStation(string locationName, bool available = true)
	{
		_snapshot = available
			? new WeatherSnapshot
			{
				IsAvailable = true,
				LocationName = locationName,
				Temperature = 20,
				Condition = WeatherCondition.Clear,
				IsDay = true,
				Unit = TemperatureUnit.Celsius,
				Days = [new WeatherForecastDay(new DateOnly(2026, 7, 15), WeatherCondition.Clear, 15, 25)]
			}
			: WeatherSnapshot.Unavailable(locationName);
	}

	public Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(_snapshot);
}

internal sealed class FakeWeatherProviderIntegration : IIntegration, IWeatherProvider, IIntegrationIconProvider
{
	private readonly IReadOnlyList<WeatherStationInstance> _instances;
	private readonly Dictionary<string, IWeatherStation> _stations;

	public FakeWeatherProviderIntegration(string id, string providerName, params string[] instanceIds)
	{
		Id = id;
		ProviderName = providerName;
		_instances = instanceIds.Select(i => new WeatherStationInstance(i, $"{providerName} {i}")).ToList();
		_stations = instanceIds.ToDictionary(i => i, i => (IWeatherStation)new FakeWeatherStation(i));
	}

	public string Id { get; }
	public LocalizedText Name => Id;
	public string Version => "1.0.0";
	public string ProviderName { get; }
	public bool IsInitialized => true;
	public IReadOnlyList<IActionDefinition> Actions => [];

	public string IconMimeType => "image/svg+xml";
	public byte[] GetIcon() => [1, 2, 3];

	public IReadOnlyList<WeatherStationInstance> GetInstances() => _instances;

	public IWeatherStation? GetStation(string instanceId)
		=> _stations.GetValueOrDefault(instanceId);

	public Task InitializeAsync(IIntegrationContext context) => Task.CompletedTask;

	public Task ShutdownAsync() => Task.CompletedTask;
}

internal sealed class FakeOpenMeteoClient : IOpenMeteoClient
{
	public List<GeocodingResult> GeocodingResults { get; set; } = [];
	public Exception? GeocodingException { get; set; }
	public OpenMeteoForecastResponse? Forecast { get; set; }

	public Task<OpenMeteoForecastResponse?> GetForecastAsync(
		double latitude,
		double longitude,
		TemperatureUnit unit,
		CancellationToken cancellationToken)
		=> Task.FromResult(Forecast);

	public Task<IReadOnlyList<GeocodingResult>> SearchLocationAsync(string query, CancellationToken cancellationToken)
	{
		if (GeocodingException is not null)
		{
			throw GeocodingException;
		}

		return Task.FromResult<IReadOnlyList<GeocodingResult>>(GeocodingResults);
	}
}
