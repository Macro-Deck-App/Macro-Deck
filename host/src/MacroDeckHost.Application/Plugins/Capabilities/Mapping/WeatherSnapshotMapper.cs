using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

public static class WeatherSnapshotMapper
{
	public static WeatherForecastDay ToDomain(WeatherForecastDayDto dto)
	{
		if (!Enum.TryParse<WeatherCondition>(dto.Condition, ignoreCase: false, out var condition))
		{
			throw new InvalidOperationException($"Unknown weather condition '{dto.Condition}'.");
		}

		return new WeatherForecastDay(dto.Date, condition, dto.Min, dto.Max);
	}

	public static WeatherHour ToDomain(WeatherHourDto dto)
	{
		ArgumentNullException.ThrowIfNull(dto);

		if (!Enum.TryParse<WeatherCondition>(dto.Condition, ignoreCase: false, out var condition))
		{
			throw new InvalidOperationException($"Unknown weather condition '{dto.Condition}'.");
		}

		return new WeatherHour(dto.Time, condition, dto.Temperature, dto.PrecipitationProbability);
	}

	public static WeatherSnapshot ToDomain(WeatherSnapshotDto dto)
	{
		if (!Enum.TryParse<WeatherCondition>(dto.Condition, ignoreCase: false, out var condition))
		{
			throw new InvalidOperationException($"Unknown weather condition '{dto.Condition}'.");
		}

		if (!Enum.TryParse<TemperatureUnit>(dto.Unit, ignoreCase: false, out var unit))
		{
			throw new InvalidOperationException($"Unknown temperature unit '{dto.Unit}'.");
		}

		return new WeatherSnapshot
		{
			IsAvailable = dto.IsAvailable,
			LocationName = dto.LocationName,
			Temperature = dto.Temperature,
			ApparentTemperature = dto.ApparentTemperature,
			Condition = condition,
			IsDay = dto.IsDay,
			Unit = unit,
			Days = [.. dto.Days.Select(ToDomain)],
			Hours = [.. dto.Hours.Select(ToDomain)],
			WindSpeed = dto.WindSpeed,
			WindDirection = dto.WindDirection,
			Humidity = dto.Humidity,
			Precipitation = dto.Precipitation,
			Sunrise = dto.Sunrise,
			Sunset = dto.Sunset
		};
	}
}
