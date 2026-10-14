using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class WeatherCatalogMapper
{
	public static WeatherStationInstance ToDomain(WeatherStationInstanceDto dto)
	{
		return new WeatherStationInstance(dto.Id, dto.DisplayName);
	}

	public static WeatherStationInstanceDto ToDto(WeatherStationInstance instance)
	{
		return new WeatherStationInstanceDto { Id = instance.Id, DisplayName = instance.DisplayName };
	}

	public static WeatherDetailsAction? ToDomain(WeatherDetailsActionDto? dto)
	{
		return dto is { ActionId.Length: > 0, StationParameter.Length: > 0 }
			? new WeatherDetailsAction(dto.ActionId, dto.StationParameter)
			: null;
	}

	public static WeatherDetailsActionDto? ToDto(WeatherDetailsAction? action)
	{
		return action is null
			? null
			: new WeatherDetailsActionDto { ActionId = action.ActionId, StationParameter = action.StationParameter };
	}
}
