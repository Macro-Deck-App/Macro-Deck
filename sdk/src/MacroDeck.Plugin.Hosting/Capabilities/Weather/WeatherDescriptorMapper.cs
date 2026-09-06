using MacroDeck.Plugin.Protocol.Capabilities.Weather;
using MacroDeck.Sdk.Weather;

namespace MacroDeck.Plugin.Hosting.Capabilities.Weather;

/// <summary>Maps the SDK's weather types to their wire DTOs.</summary>
internal static class WeatherDescriptorMapper
{
	public static WeatherStationInstanceDto ToDto(WeatherStationInstance instance)
		=> new() { Id = instance.Id, DisplayName = instance.DisplayName };

	public static WeatherForecastDayDto ToDto(WeatherForecastDay day)
		=> new() { Date = day.Date, Condition = day.Condition.ToString(), Min = day.Min, Max = day.Max };

	public static WeatherSnapshotDto ToDto(WeatherSnapshot snapshot)
		=> new()
		{
			IsAvailable = snapshot.IsAvailable,
			LocationName = snapshot.LocationName,
			Temperature = snapshot.Temperature,
			ApparentTemperature = snapshot.ApparentTemperature,
			Condition = snapshot.Condition.ToString(),
			IsDay = snapshot.IsDay,
			Unit = snapshot.Unit.ToString(),
			Days = [.. snapshot.Days.Select(ToDto)],
			Hours = [.. snapshot.Hours.Select(ToDto)],
			WindSpeed = snapshot.WindSpeed,
			WindDirection = snapshot.WindDirection,
			Humidity = snapshot.Humidity,
			Precipitation = snapshot.Precipitation,
			Sunrise = snapshot.Sunrise,
			Sunset = snapshot.Sunset
		};

	public static WeatherHourDto ToDto(WeatherHour hour)
	{
		ArgumentNullException.ThrowIfNull(hour);

		return new WeatherHourDto
		{
			Time = hour.Time,
			Condition = hour.Condition.ToString(),
			Temperature = hour.Temperature,
			PrecipitationProbability = hour.PrecipitationProbability
		};
	}
}
