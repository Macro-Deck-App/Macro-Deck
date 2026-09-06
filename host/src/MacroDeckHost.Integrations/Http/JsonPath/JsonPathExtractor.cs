using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Http.JsonPath;

internal static class JsonPathExtractor
{
	public static bool TryExtract(string json, string path, out VariableType type, out object value)
	{
		type = VariableType.Text;
		value = string.Empty;

		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(json);
		}
		catch (JsonException)
		{
			return false;
		}

		using (document)
		{
			if (!TryNavigate(document.RootElement, path, out var element))
			{
				return false;
			}

			(type, value) = MapToVariable(element);
			return true;
		}
	}

	private static bool TryNavigate(JsonElement root, string path, out JsonElement result)
	{
		result = root;

		var trimmed = path.Trim();
		if (trimmed.Length == 0)
		{
			return false;
		}

		if (trimmed[0] == '$')
		{
			trimmed = trimmed[1..];
			if (trimmed.StartsWith('.'))
			{
				trimmed = trimmed[1..];
			}
		}

		if (trimmed.Length == 0)
		{
			result = root;
			return true;
		}

		var current = root;
		foreach (var segment in trimmed.Split('.'))
		{
			if (!TryDescend(current, segment, out current))
			{
				return false;
			}
		}

		result = current;
		return true;
	}

	private static bool TryDescend(JsonElement current, string segment, out JsonElement result)
	{
		result = current;

		var bracketIndex = segment.IndexOf('[', StringComparison.Ordinal);
		var name = bracketIndex < 0 ? segment : segment[..bracketIndex];
		var indices = bracketIndex < 0 ? string.Empty : segment[bracketIndex..];

		if (name.Length > 0)
		{
			if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(name, out var property))
			{
				return false;
			}

			current = property;
		}
		else if (indices.Length == 0)
		{
			return false;
		}

		var position = 0;
		while (position < indices.Length)
		{
			if (indices[position] != '[')
			{
				return false;
			}

			var close = indices.IndexOf(']', position);
			if (close < 0)
			{
				return false;
			}

			var digits = indices[(position + 1)..close];
			if (digits.Length == 0 ||
				!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var index))
			{
				return false;
			}

			if (current.ValueKind != JsonValueKind.Array || index >= current.GetArrayLength())
			{
				return false;
			}

			current = current[index];
			position = close + 1;
		}

		result = current;
		return true;
	}

	private static (VariableType Type, object Value) MapToVariable(JsonElement element) => element.ValueKind switch
	{
		JsonValueKind.Number => (VariableType.Numeric, element.GetDouble()),
		JsonValueKind.True => (VariableType.Boolean, true),
		JsonValueKind.False => (VariableType.Boolean, false),
		JsonValueKind.String => (VariableType.Text, (object)(element.GetString() ?? string.Empty)),
		JsonValueKind.Null => (VariableType.Text, string.Empty),
		_ => (VariableType.Text, element.GetRawText())
	};
}
