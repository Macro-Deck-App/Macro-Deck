using System.Globalization;
using System.Text.Json;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Application.Actions;

public static class ActionParameterLiteralCoercion
{
	public static Dictionary<string, object> Build(
		IReadOnlyList<ActionParameter> parameters,
		IReadOnlyDictionary<string, JsonElement>? values)
	{
		var result = new Dictionary<string, object>();
		foreach (var parameter in parameters)
		{
			if (string.IsNullOrWhiteSpace(parameter.Name))
			{
				continue;
			}

			if (values is not null && values.TryGetValue(parameter.Name, out var element))
			{
				result[parameter.Name] = CoerceElement(parameter.Type, element);
			}
			else
			{
				result[parameter.Name] = parameter.DefaultValue ?? DefaultFor(parameter.Type);
			}
		}

		return result;
	}

	private static object CoerceElement(ActionParameterType type, JsonElement value)
	{
		if (value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
		{
			return DefaultFor(type);
		}

		return value.ValueKind switch
		{
			JsonValueKind.String => CoerceString(type, value.GetString() ?? string.Empty),
			JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Object => CoerceObject(type, value),
			JsonValueKind.Array => CoerceArray(type, value),
			_ => value.ToString()
		};
	}

	private static object CoerceString(ActionParameterType type, string value)
	{
		return type switch
		{
			ActionParameterType.Number
				when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
				=> number,
			ActionParameterType.Duration => ParseDurationMilliseconds(value),
			ActionParameterType.Boolean when bool.TryParse(value, out var boolean) => boolean,
			_ => value
		};
	}

	private static object CoerceObject(ActionParameterType type, JsonElement value)
	{
		if (type is ActionParameterType.KeyValue)
		{
			var map = new Dictionary<string, string>();
			foreach (var property in value.EnumerateObject())
			{
				map[property.Name] = property.Value.ValueKind == JsonValueKind.String
					? property.Value.GetString() ?? string.Empty
					: property.Value.ToString();
			}

			return map;
		}

		return ConvertJsonElement(value) ?? value.GetRawText();
	}

	private static object CoerceArray(ActionParameterType type, JsonElement value)
	{
		if (type is ActionParameterType.MultiSelect)
		{
			return value.EnumerateArray()
				.Select(element => element.ValueKind == JsonValueKind.String
					? element.GetString() ?? string.Empty
					: element.ToString())
				.ToArray();
		}

		return ConvertJsonElement(value) ?? value.GetRawText();
	}

	private static object DefaultFor(ActionParameterType type)
	{
		return type switch
		{
			ActionParameterType.Number or ActionParameterType.Duration => 0d,
			ActionParameterType.Boolean => false,
			ActionParameterType.MultiSelect => Array.Empty<string>(),
			ActionParameterType.KeyValue => new Dictionary<string, string>(),
			_ => string.Empty
		};
	}

	private static object? ConvertJsonElement(JsonElement element)
	{
		return element.ValueKind switch
		{
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number => element.TryGetInt64(out var integer) ? integer : element.GetDouble(),
			JsonValueKind.True => true,
			JsonValueKind.False => false,
			JsonValueKind.Null or JsonValueKind.Undefined => null,
			JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElement).ToList(),
			JsonValueKind.Object => element.EnumerateObject()
				.ToDictionary(property => property.Name, property => ConvertJsonElement(property.Value)),
			_ => element.GetRawText()
		};
	}

	private static double ParseDurationMilliseconds(string value)
	{
		var trimmed = value.Trim();
		if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain))
		{
			return plain;
		}

		var (factor, suffixLength) = trimmed switch
		{
			_ when trimmed.EndsWith("ms", StringComparison.OrdinalIgnoreCase) => (1d, 2),
			_ when trimmed.EndsWith('s') || trimmed.EndsWith('S') => (1_000d, 1),
			_ when trimmed.EndsWith('m') || trimmed.EndsWith('M') => (60_000d, 1),
			_ when trimmed.EndsWith('h') || trimmed.EndsWith('H') => (3_600_000d, 1),
			_ => (0d, 0)
		};

		if (suffixLength > 0 &&
			double.TryParse(trimmed[..^suffixLength], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
		{
			return number * factor;
		}

		return 0d;
	}
}
