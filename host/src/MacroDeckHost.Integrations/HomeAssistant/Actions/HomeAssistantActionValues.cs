using System.Globalization;
using System.Text.Json;

namespace MacroDeckHost.Integrations.HomeAssistant.Actions;

internal static class HomeAssistantActionValues
{
	public static string? ReadText(IReadOnlyDictionary<string, object> parameters, string name)
	{
		var value = parameters.GetValueOrDefault(name);
		var text = value switch
		{
			null => null,
			string s => s,
			bool b => b ? "true" : "false",
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString()
		};

		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}

	public static bool ReadBool(IReadOnlyDictionary<string, object> parameters, string name, bool fallback = false)
		=> parameters.GetValueOrDefault(name) switch
		{
			null => fallback,
			bool b => b,
			string s when bool.TryParse(s, out var parsed) => parsed,
			_ => fallback
		};

	public static double? ReadNumber(IReadOnlyDictionary<string, object> parameters, string name)
		=> parameters.GetValueOrDefault(name) switch
		{
			null => null,
			double d => d,
			float f => f,
			int i => i,
			long l => l,
			decimal m => (double)m,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
				=> parsed,
			_ => null
		};

	public static double? ReadNumber(
		IReadOnlyDictionary<string, object> parameters,
		string name,
		double min,
		double max)
		=> ReadNumber(parameters, name) is { } value ? Math.Clamp(value, min, max) : null;

	public static IReadOnlyList<string> ReadList(IReadOnlyDictionary<string, object> parameters, string name)
		=> ReadList(parameters.GetValueOrDefault(name));

	public static IReadOnlyList<string> ReadList(object? value)
	{
		return value switch
		{
			null => [],
			IEnumerable<string> items => Clean(items),
			JsonElement element => FromJsonArray(element),
			string text => FromText(text),
			_ => []
		};
	}

	public static IReadOnlyDictionary<string, object?>? ReadData(
		IReadOnlyDictionary<string, object> parameters,
		string name)
	{
		var value = parameters.GetValueOrDefault(name);
		var data = value switch
		{
			IReadOnlyDictionary<string, string> map => FromStringMap(map),
			IReadOnlyDictionary<string, object?> map => new Dictionary<string, object?>(map, StringComparer.Ordinal),
			JsonElement element => FromJsonObject(element),
			string text => FromJsonText(text),
			_ => null
		};

		return data is { Count: > 0 } ? data : null;
	}

	public static IReadOnlyList<int>? ReadRgb(string? color)
	{
		var value = (color ?? string.Empty).Trim().TrimStart('#');
		if (value.Length != 6)
		{
			return null;
		}

		if (!int.TryParse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var red) ||
			!int.TryParse(value[2..4], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var green) ||
			!int.TryParse(value[4..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var blue))
		{
			return null;
		}

		return [red, green, blue];
	}

	private static List<string> Clean(IEnumerable<string> items)
	{
		var result = new List<string>();
		foreach (var item in items)
		{
			if (!string.IsNullOrWhiteSpace(item))
			{
				result.Add(item.Trim());
			}
		}

		return result;
	}

	private static List<string> FromText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return [];
		}

		var trimmed = text.Trim();
		if (trimmed.StartsWith('['))
		{
			try
			{
				using var document = JsonDocument.Parse(trimmed);
				return FromJsonArray(document.RootElement);
			}
			catch (JsonException)
			{
				return [];
			}
		}

		return Clean(trimmed.Split(','));
	}

	private static List<string> FromJsonArray(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		var result = new List<string>(element.GetArrayLength());
		foreach (var entry in element.EnumerateArray())
		{
			if (entry.ValueKind == JsonValueKind.String && entry.GetString() is { Length: > 0 } text)
			{
				result.Add(text);
			}
		}

		return result;
	}

	private static Dictionary<string, object?> FromStringMap(IReadOnlyDictionary<string, string> map)
	{
		var result = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var (key, value) in map)
		{
			if (!string.IsNullOrWhiteSpace(key))
			{
				result[key] = value;
			}
		}

		return result;
	}

	private static Dictionary<string, object?>? FromJsonText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(text);
			return FromJsonObject(document.RootElement);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static Dictionary<string, object?>? FromJsonObject(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var result = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var property in element.EnumerateObject())
		{
			result[property.Name] = ToPrimitive(property.Value);
		}

		return result;
	}

	private static object? ToPrimitive(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.String => value.GetString(),
		JsonValueKind.True => true,
		JsonValueKind.False => false,
		JsonValueKind.Number => value.TryGetInt64(out var integer) ? integer : value.GetDouble(),
		JsonValueKind.Null => null,
		_ => value.Clone()
	};
}
