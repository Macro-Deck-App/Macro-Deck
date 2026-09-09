using System.Text.Json;

namespace MacroDeckHost.Widgets.Weather;

public sealed record WeatherWidgetData
{
	public string? InstanceId { get; init; }

	public bool ShowIcon { get; init; } = true;

	public bool ShowTemperature { get; init; } = true;

	public bool ShowCondition { get; init; } = true;

	public bool ShowLocation { get; init; } = true;

	public bool ShowForecast { get; init; } = true;

	public bool AnimateIcon { get; init; } = true;

	public int ForecastDays { get; init; } = 5;

	public static WeatherWidgetData Parse(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object)
		{
			return new WeatherWidgetData();
		}

		return new WeatherWidgetData
		{
			InstanceId = ReadString(data, "instanceId"),
			ShowIcon = ReadBool(data, "showIcon") ?? true,
			ShowTemperature = ReadBool(data, "showTemperature") ?? true,
			ShowCondition = ReadBool(data, "showCondition") ?? true,
			// Widgets saved before the two options split stored one showCondition for both lines, so an
			// absent showLocation has to keep meaning whatever showCondition said.
			ShowLocation = ReadBool(data, "showLocation") ?? ReadBool(data, "showCondition") ?? true,
			ShowForecast = ReadBool(data, "showForecast") ?? true,
			AnimateIcon = ReadBool(data, "animateIcon") ?? true,
			ForecastDays = Math.Clamp(ReadInt(data, "forecastDays") ?? 5, 1, 7),
		};
	}

	private static string? ReadString(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static bool? ReadBool(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: null;

	private static int? ReadInt(JsonElement data, string name)
		=> data.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetInt32(out var number)
				? number
				: null;
}
