using MacroDeckHost.Application.Weather;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Ui.Transport.Messages.Weather;

public class GetWeatherInstancesRequest;

public class WeatherInstanceDto
{
	public string InstanceId { get; set; } = string.Empty;

	public string IntegrationId { get; set; } = string.Empty;

	public LocalizedText ProviderName { get; set; }

	public string DisplayName { get; set; } = string.Empty;

	public bool HasIcon { get; set; }

	public static WeatherInstanceDto From(WeatherStationDescriptor descriptor)
		=> new()
		{
			InstanceId = descriptor.InstanceId,
			IntegrationId = descriptor.IntegrationId,
			ProviderName = descriptor.ProviderName,
			DisplayName = descriptor.DisplayName,
			HasIcon = descriptor.HasIcon
		};
}

public class GetWeatherInstancesResponse
{
	public List<WeatherInstanceDto> Instances { get; set; } = new();
}
