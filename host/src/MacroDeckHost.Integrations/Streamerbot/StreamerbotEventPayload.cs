using System.Text.Json;

namespace MacroDeckHost.Integrations.Streamerbot;

internal static class StreamerbotEventPayload
{
	internal const int MaxDepth = 3;

	internal const int MaxParameters = 64;

	private static readonly string[] _reservedKeys = ["source", "type", "data"];

	private static readonly string[] _userKeys =
	[
		"displayName",
		"userName",
		"username",
		"user_name",
		"user_login",
		"user",
		"user.displayName",
		"user.name",
		"user.login",
		"message.displayName",
		"message.userName",
		"message.username",
		"from_broadcaster_user_name",
		"recipientDisplayName"
	];

	private static readonly string[] _messageKeys = ["message", "message.message", "user_input"];

	public static IReadOnlyDictionary<string, object?> Build(string source, string type, JsonElement data)
	{
		var parameters = new Dictionary<string, object?>(StringComparer.Ordinal);
		Flatten(data, prefix: null, depth: 0, parameters);

		Normalise(parameters, "user", _userKeys);
		Normalise(parameters, "message", _messageKeys);

		parameters["source"] = source;
		parameters["type"] = type;
		parameters["data"] = data.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
			? string.Empty
			: data.GetRawText();

		return parameters;
	}

	private static void Flatten(JsonElement element, string? prefix, int depth, Dictionary<string, object?> target)
	{
		if (element.ValueKind != JsonValueKind.Object || depth >= MaxDepth)
		{
			return;
		}

		foreach (var property in element.EnumerateObject())
		{
			if (target.Count >= MaxParameters)
			{
				return;
			}

			var name = prefix is null ? property.Name : $"{prefix}.{property.Name}";
			switch (property.Value.ValueKind)
			{
				case JsonValueKind.Object:
					Flatten(property.Value, name, depth + 1, target);
					break;
				case JsonValueKind.Array:
					// Deliberately skipped; the full payload stays available as `data`.
					break;
				default:
					if (!IsReserved(name))
					{
						target[name] = ToPrimitive(property.Value);
					}

					break;
			}
		}
	}

	private static object? ToPrimitive(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.String => value.GetString(),
		JsonValueKind.True => true,
		JsonValueKind.False => false,
		JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
		_ => null
	};

	private static void Normalise(Dictionary<string, object?> parameters, string name, string[] candidates)
	{
		if (HasValue(parameters, name))
		{
			return;
		}

		foreach (var candidate in candidates)
		{
			if (parameters.TryGetValue(candidate, out var value) && value is string { Length: > 0 } text)
			{
				parameters[name] = text;
				return;
			}
		}
	}

	private static bool HasValue(Dictionary<string, object?> parameters, string name)
		=> parameters.TryGetValue(name, out var value) && value is not null and not "";

	private static bool IsReserved(string name) => Array.IndexOf(_reservedKeys, name) >= 0;
}
