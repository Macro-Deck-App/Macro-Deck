using System.Globalization;
using System.Text;
using System.Text.Json;

namespace MacroDeckHost.Integrations.Twitch;

internal static class TwitchEventPayload
{
	private static readonly Dictionary<string, string[]> _aliases = new(StringComparer.Ordinal)
	{
		["userId"] = ["user_id", "chatter_user_id", "target_user_id"],
		["userLogin"] = ["user_login", "chatter_user_login", "target_user_login"],
		["userName"] = ["user_name", "chatter_user_name", "target_user_name"],
		["moderatorLogin"] = ["moderator_user_login"],
		["moderatorName"] = ["moderator_user_name"],
		["fromUserId"] = ["from_broadcaster_user_id"],
		["fromUserLogin"] = ["from_broadcaster_user_login"],
		["fromUserName"] = ["from_broadcaster_user_name"],
		["toUserId"] = ["to_broadcaster_user_id"],
		["toUserLogin"] = ["to_broadcaster_user_login"],
		["toUserName"] = ["to_broadcaster_user_name"],
		["requesterName"] = ["requester_user_name"],
		["message"] = ["message.text", "message"],
		["streamType"] = ["type"],
		["contentLabels"] = ["content_classification_labels"],
		["rewardId"] = ["reward.id"],
		["rewardTitle"] = ["reward.title"],
		["rewardCost"] = ["reward.cost"],
		["redemptionId"] = ["id"],
		["pollId"] = ["id"],
		["predictionId"] = ["id"],
		["goalId"] = ["id"],
		["goalType"] = ["type"],
		["choices"] = ["choices"],
		["outcomes"] = ["outcomes"]
	};

	public static Dictionary<string, object?> Build(
		IReadOnlyList<string> declared,
		JsonElement eventElement,
		TwitchAccount account)
	{
		var values = new Dictionary<string, object?>(StringComparer.Ordinal)
		{
			["account"] = account.UserId,
			["accountLogin"] = account.Login,
			["accountName"] = account.DisplayName
		};

		foreach (var name in declared)
		{
			if (values.ContainsKey(name))
			{
				continue;
			}

			values[name] = Resolve(name, eventElement);
		}

		return values;
	}

	public static Dictionary<string, object?> BuildGeneric(
		string subscriptionType,
		string? version,
		string messageId,
		JsonElement eventElement,
		TwitchAccount account)
		=> new(StringComparer.Ordinal)
		{
			["account"] = account.UserId,
			["accountLogin"] = account.Login,
			["accountName"] = account.DisplayName,
			["type"] = subscriptionType,
			["version"] = version ?? string.Empty,
			["messageId"] = messageId,
			["userLogin"] = Resolve("userLogin", eventElement) ?? string.Empty,
			["userName"] = Resolve("userName", eventElement) ?? string.Empty,
			["message"] = Resolve("message", eventElement) ?? string.Empty,
			["data"] = eventElement.ValueKind is JsonValueKind.Undefined ? "{}" : eventElement.GetRawText()
		};

	private static object? Resolve(string name, JsonElement root)
	{
		if (root.ValueKind is not JsonValueKind.Object)
		{
			return null;
		}

		switch (name)
		{
			case "winningChoice":
				return Winner(root, "choices", "votes")?.Title;

			case "winningVotes":
				return Winner(root, "choices", "votes")?.Votes;

			case "winningOutcome":
				return WinningOutcome(root);

			default:
				break;
		}

		if (_aliases.TryGetValue(name, out var candidates))
		{
			foreach (var candidate in candidates)
			{
				if (ReadPath(root, candidate) is { } aliased)
				{
					return aliased;
				}
			}
		}

		return ReadPath(root, ToSnakeCase(name));
	}

	private static object? ReadPath(JsonElement root, string path)
	{
		var current = root;
		foreach (var segment in path.Split('.'))
		{
			if (current.ValueKind is not JsonValueKind.Object || !current.TryGetProperty(segment, out var next))
			{
				return null;
			}

			current = next;
		}

		return ToPrimitive(current);
	}

	private static object? ToPrimitive(JsonElement element)
		=> element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Number => element.TryGetInt64(out var whole)
				? whole
				: element.GetDouble(),

			JsonValueKind.Array => JoinArray(element),
			JsonValueKind.Object => Describe(element),
			_ => null
		};

	private static string JoinArray(JsonElement array)
	{
		var builder = new StringBuilder();
		foreach (var item in array.EnumerateArray())
		{
			if (builder.Length > 0)
			{
				builder.Append(", ");
			}

			builder.Append(item.ValueKind is JsonValueKind.Object
				? ReadString(item, "title") ?? ReadString(item, "name") ?? ReadString(item, "id") ?? string.Empty
				: ToPrimitive(item)?.ToString() ?? string.Empty);
		}

		return builder.ToString();
	}

	private static string? Describe(JsonElement element)
		=> ReadString(element, "title") ?? ReadString(element, "text") ?? ReadString(element, "name");

	private static (string? Title, long Votes)? Winner(JsonElement root, string arrayName, string countName)
	{
		if (!root.TryGetProperty(arrayName, out var array) || array.ValueKind is not JsonValueKind.Array)
		{
			return null;
		}

		(string? Title, long Votes)? best = null;
		foreach (var item in array.EnumerateArray())
		{
			if (item.ValueKind is not JsonValueKind.Object)
			{
				continue;
			}

			var votes = item.TryGetProperty(countName, out var value) && value.TryGetInt64(out var parsed)
				? parsed
				: 0;

			if (best is null || votes > best.Value.Votes)
			{
				best = (ReadString(item, "title"), votes);
			}
		}

		return best;
	}

	private static string? WinningOutcome(JsonElement root)
	{
		var winningId = ReadString(root, "winning_outcome_id");
		if (winningId is null ||
			!root.TryGetProperty("outcomes", out var outcomes) ||
			outcomes.ValueKind is not JsonValueKind.Array)
		{
			return null;
		}

		foreach (var outcome in outcomes.EnumerateArray())
		{
			if (string.Equals(ReadString(outcome, "id"), winningId, StringComparison.Ordinal))
			{
				return ReadString(outcome, "title");
			}
		}

		return null;
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.ValueKind is JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind is JsonValueKind.String
				? value.GetString()
				: null;

	private static string ToSnakeCase(string name)
	{
		var builder = new StringBuilder(name.Length + 6);
		foreach (var character in name)
		{
			if (char.IsUpper(character))
			{
				if (builder.Length > 0)
				{
					builder.Append('_');
				}

				builder.Append(char.ToLower(character, CultureInfo.InvariantCulture));
				continue;
			}

			builder.Append(character);
		}

		return builder.ToString();
	}
}
