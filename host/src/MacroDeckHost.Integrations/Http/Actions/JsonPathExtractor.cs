using System.Text.Json;
using MacroDeck.Sdk.Variables;

namespace MacroDeckHost.Integrations.Http.Actions;

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
		if (!JsonPathSyntax.TryParse(path, out var segments))
		{
			return false;
		}

		var current = root;
		foreach (var segment in segments)
		{
			if (!TryDescend(current, segment, out current))
			{
				return false;
			}
		}

		result = current;
		return true;
	}

	private static bool TryDescend(JsonElement current, JsonPathSegment segment, out JsonElement result)
	{
		result = current;
		if (segment.IsIndex)
		{
			if (current.ValueKind != JsonValueKind.Array || segment.Index >= current.GetArrayLength())
			{
				return false;
			}

			result = current[segment.Index];
			return true;
		}

		if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment.Property!, out var property))
		{
			return false;
		}

		result = property;
		return true;
	}

	private static (VariableType Type, object Value) MapToVariable(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.Number => (VariableType.Numeric, element.GetDouble()),
			JsonValueKind.True => (VariableType.Boolean, true),
			JsonValueKind.False => (VariableType.Boolean, false),
			JsonValueKind.String => (VariableType.Text, (object)(element.GetString() ?? string.Empty)),
			JsonValueKind.Null => (VariableType.Text, string.Empty),
			_ => (VariableType.Text, element.GetRawText())
		};
	}
}
