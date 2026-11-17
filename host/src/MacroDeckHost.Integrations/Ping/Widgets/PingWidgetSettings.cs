using System.Text.Json;
using MacroDeckHost.Integrations.Ui;

namespace MacroDeckHost.Integrations.Ping.Widgets;

internal sealed record PingWidgetOptions
{
	public string? TargetId { get; init; }

	public string? DisplayName { get; init; }

	public bool ShowLatency { get; init; } = true;

	public bool ShowPacketLoss { get; init; } = true;

	public bool ShowHistory { get; init; } = true;
}

internal static class PingWidgetSettings
{
	public static PingWidgetOptions Parse(JsonElement data)
	{
		return new PingWidgetOptions
		{
			TargetId = ReadString(data, PingWidgetType.TargetKey) is { Length: > 0 } id ? id : null,
			DisplayName = ReadString(data, PingWidgetType.DisplayNameKey)?.Trim() is { Length: > 0 } name
				? name
				: null,
			ShowLatency = ReadBool(data, PingWidgetType.ShowLatencyKey) ?? true,
			ShowPacketLoss = ReadBool(data, PingWidgetType.ShowPacketLossKey) ?? true,
			ShowHistory = ReadBool(data, PingWidgetType.ShowHistoryKey) ?? true
		};
	}

	public static string? BackgroundColor(JsonElement data)
	{
		return WidgetBackgroundColor.Normalize(ReadString(data, PingWidgetType.BackgroundColorKey));
	}

	private static string? ReadString(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
	}

	private static bool? ReadBool(JsonElement data, string name)
	{
		return data.ValueKind == JsonValueKind.Object
			&& data.TryGetProperty(name, out var value)
			&& value.ValueKind is JsonValueKind.True or JsonValueKind.False
				? value.GetBoolean()
				: null;
	}
}
