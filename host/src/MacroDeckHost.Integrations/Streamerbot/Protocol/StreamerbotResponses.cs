using System.Text.Json;

namespace MacroDeckHost.Integrations.Streamerbot.Protocol;

internal static class StreamerbotResponses
{
	private static readonly string[] _broadcasterNameKeys =
	[
		"broadcastUserName", "broadcasterUserName", "broadcastUser", "broadcasterLogin"
	];

	public static IReadOnlyDictionary<string, IReadOnlyList<string>> EmptyEventCatalog { get; }
		= new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

	public static StreamerbotInstanceInfo? ReadInstanceInfo(JsonElement root)
	{
		if (!root.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		return new StreamerbotInstanceInfo(ReadString(info, "name"),
			ReadString(info, "version"),
			ReadString(info, "instanceId"),
			ReadString(info, "os"));
	}

	public static IReadOnlyList<StreamerbotActionInfo> ReadActions(JsonElement root)
	{
		if (!root.TryGetProperty("actions", out var actions) || actions.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<StreamerbotActionInfo>(actions.GetArrayLength());
		foreach (var action in actions.EnumerateArray())
		{
			if (action.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			var id = ReadString(action, "id");
			var name = ReadString(action, "name");
			if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(name))
			{
				continue;
			}

			var enabled = !action.TryGetProperty("enabled", out var enabledElement) ||
				enabledElement.ValueKind != JsonValueKind.False;

			result.Add(new StreamerbotActionInfo(id, name, ReadString(action, "group"), enabled));
		}

		return result;
	}

	public static IReadOnlyList<StreamerbotCodeTrigger> ReadCodeTriggers(JsonElement root)
	{
		if (!root.TryGetProperty("triggers", out var triggers) || triggers.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<StreamerbotCodeTrigger>(triggers.GetArrayLength());
		foreach (var trigger in triggers.EnumerateArray())
		{
			if (trigger.ValueKind == JsonValueKind.Object && ReadString(trigger, "name") is { Length: > 0 } name)
			{
				result.Add(new StreamerbotCodeTrigger(name, ReadString(trigger, "category")));
			}
		}

		return result;
	}

	public static IReadOnlyDictionary<string, IReadOnlyList<string>> ReadEventCatalog(JsonElement root)
	{
		if (!root.TryGetProperty("events", out var events) || events.ValueKind != JsonValueKind.Object)
		{
			return EmptyEventCatalog;
		}

		var result = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
		foreach (var source in events.EnumerateObject())
		{
			if (source.Value.ValueKind != JsonValueKind.Array)
			{
				continue;
			}

			var types = new List<string>(source.Value.GetArrayLength());
			foreach (var type in source.Value.EnumerateArray())
			{
				if (type.ValueKind == JsonValueKind.String && type.GetString() is { Length: > 0 } name)
				{
					types.Add(name);
				}
			}

			if (types.Count > 0)
			{
				result[source.Name] = types;
			}
		}

		return result;
	}

	public static StreamerbotBroadcaster? ReadBroadcaster(JsonElement root)
	{
		if (!root.TryGetProperty("platforms", out var platforms) || platforms.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var connected = ReadConnectedPlatform(root);
		foreach (var platform in platforms.EnumerateObject())
		{
			if (platform.Value.ValueKind != JsonValueKind.Object)
			{
				continue;
			}

			if (connected is not null && !string.Equals(platform.Name, connected, StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			foreach (var key in _broadcasterNameKeys)
			{
				if (ReadString(platform.Value, key) is { Length: > 0 } name)
				{
					return new StreamerbotBroadcaster(platform.Name, name);
				}
			}

			return new StreamerbotBroadcaster(platform.Name, null);
		}

		return connected is null ? null : new StreamerbotBroadcaster(connected, null);
	}

	public static JsonElement? ReadGlobalValue(JsonElement root, string name)
	{
		if (root.TryGetProperty("variables", out var variables))
		{
			if (variables.ValueKind == JsonValueKind.Object &&
				variables.TryGetProperty(name, out var byName) &&
				byName.TryGetProperty("value", out var value))
			{
				return value;
			}

			if (variables.ValueKind == JsonValueKind.Array)
			{
				foreach (var entry in variables.EnumerateArray())
				{
					if (string.Equals(ReadString(entry, "name"), name, StringComparison.Ordinal) &&
						entry.TryGetProperty("value", out var entryValue))
					{
						return entryValue;
					}
				}
			}
		}

		if (root.TryGetProperty("variable", out var single) &&
			single.ValueKind == JsonValueKind.Object &&
			single.TryGetProperty("value", out var singleValue))
		{
			return singleValue;
		}

		return null;
	}

	private static string? ReadConnectedPlatform(JsonElement root)
	{
		if (!root.TryGetProperty("connected", out var connected) || connected.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		foreach (var platform in connected.EnumerateArray())
		{
			if (platform.ValueKind == JsonValueKind.String && platform.GetString() is { Length: > 0 } name)
			{
				return name;
			}
		}

		return null;
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;
}
