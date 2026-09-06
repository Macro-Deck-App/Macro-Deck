using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class WeatherCatalogMapper
{
	public static WeatherStationInstance ToDomain(WeatherStationInstanceDto dto) => new(dto.Id, dto.DisplayName);

	public static WeatherStationInstanceDto ToDto(WeatherStationInstance instance)
		=> new() { Id = instance.Id, DisplayName = instance.DisplayName };
}
