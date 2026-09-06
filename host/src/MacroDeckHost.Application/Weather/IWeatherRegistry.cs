using MacroDeck.Sdk.Weather;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Weather;

public sealed record WeatherStationDescriptor(
	string InstanceId,
	string IntegrationId,
	LocalizedText ProviderName,
	string DisplayName,
	bool HasIcon);

public interface IWeatherRegistry
{
	IReadOnlyList<WeatherStationDescriptor> GetInstances();

	IWeatherStation? GetStation(string instanceId);

	IWeatherStation? DefaultStation { get; }
}
