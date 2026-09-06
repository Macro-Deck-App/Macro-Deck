using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;

namespace MacroDeckHost.Integrations.HomeAssistant;

internal static class HomeAssistantEventPayload
{
	public static IReadOnlyDictionary<string, object?> StateChanged(
		string entityId,
		HomeAssistantEntityState? newState,
		string? fromState,
		string? area)
		=> new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["entityId"] = entityId,
			["domain"] = HomeAssistantEntityState.DomainOf(entityId),
			["toState"] = newState?.State ?? string.Empty,
			["fromState"] = fromState ?? string.Empty,
			["friendlyName"] = newState?.FriendlyName ?? string.Empty,
			["area"] = area ?? string.Empty,
			["unit"] = newState?.UnitOfMeasurement ?? string.Empty,
			["attributes"] = newState?.AttributesJson ?? "{}"
		};

	public static IReadOnlyDictionary<string, object?> Event(string eventType, JsonElement data)
	{
		var entityId = data.ValueKind == JsonValueKind.Object &&
			data.TryGetProperty("entity_id", out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

		return new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["eventType"] = eventType,
			["entityId"] = entityId ?? string.Empty,
			["data"] = data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
				? string.Empty
				: data.GetRawText()
		};
	}
}
