using System.Text.Json;

namespace MacroDeckHost.Integrations.Streaming.Widgets;

internal static class StreamWidgetJson
{
	public static string? ReadString(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
	}

	public static bool? ReadBool(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
				? value.GetBoolean()
				: null;
	}

	public static double? ReadDouble(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.Number
			&& value.TryGetDouble(out var number)
			&& double.IsFinite(number)
				? number
				: null;
	}
}
