using System.Text.Json;

namespace MacroDeckHost.Integrations.HomeAssistant.Protocol;

internal sealed record HomeAssistantHello(string? Version);

internal enum HomeAssistantAuthStatus
{
	Ok,
	Invalid,
	Closed
}

internal sealed record HomeAssistantAuthOutcome(HomeAssistantAuthStatus Status, string? Version, string? Message);

internal sealed record HomeAssistantEntityState(string EntityId, string State, JsonElement Attributes)
{
	public string Domain => DomainOf(EntityId);

	public string? FriendlyName => ReadString("friendly_name");

	public string? UnitOfMeasurement => ReadString("unit_of_measurement");

	public string AttributesJson => Attributes.ValueKind == JsonValueKind.Object ? Attributes.GetRawText() : "{}";

	public static string DomainOf(string entityId)
	{
		var dot = entityId.IndexOf('.', StringComparison.Ordinal);
		return dot > 0 ? entityId[..dot] : string.Empty;
	}

	public IReadOnlyList<string> AttributeNames()
	{
		if (Attributes.ValueKind != JsonValueKind.Object)
		{
			return [];
		}

		var names = new List<string>();
		foreach (var property in Attributes.EnumerateObject())
		{
			names.Add(property.Name);
		}

		return names;
	}

	public string? ReadAttribute(string name)
	{
		if (Attributes.ValueKind != JsonValueKind.Object || !Attributes.TryGetProperty(name, out var value))
		{
			return null;
		}

		return value.ValueKind switch
		{
			JsonValueKind.String => value.GetString(),
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			_ => value.GetRawText()
		};
	}

	public IReadOnlyList<string> ReadStringList(string name)
	{
		if (Attributes.ValueKind != JsonValueKind.Object ||
			!Attributes.TryGetProperty(name, out var value) ||
			value.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<string>(value.GetArrayLength());
		foreach (var entry in value.EnumerateArray())
		{
			if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } text)
			{
				result.Add(text);
			}
		}

		return result;
	}

	private string? ReadString(string name)
		=> Attributes.ValueKind == JsonValueKind.Object &&
			Attributes.TryGetProperty(name, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
}

internal sealed record HomeAssistantAreaInfo(string AreaId, string Name);

internal sealed record HomeAssistantDeviceInfo(string DeviceId, string Name, string? AreaId);

internal sealed record HomeAssistantEventMessage(string EventType, JsonElement Data);
