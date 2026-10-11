using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class WeatherCatalogMapper
{
	public static WeatherStationInstance ToDomain(WeatherStationInstanceDto dto) => new(dto.Id, dto.DisplayName);

	public static WeatherStationInstanceDto ToDto(WeatherStationInstance instance)
		=> new() { Id = instance.Id, DisplayName = instance.DisplayName };

	public static WeatherDetailsAction? ToDomain(WeatherDetailsActionDto? dto)
		=> dto is { ActionId.Length: > 0, StationParameter.Length: > 0 }
			? new WeatherDetailsAction(dto.ActionId, dto.StationParameter)
			: null;

	public static WeatherDetailsActionDto? ToDto(WeatherDetailsAction? action)
		=> action is null ? null : new() { ActionId = action.ActionId, StationParameter = action.StationParameter };
}
