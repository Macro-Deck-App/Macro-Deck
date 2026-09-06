using System.Globalization;
using System.Text.Json;

namespace MacroDeckHost.Integrations.Streamerbot.Actions;

internal static class StreamerbotActionValues
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
			string s when string.IsNullOrWhiteSpace(s) => fallback,
			_ => fallback
		};

	public static IReadOnlyDictionary<string, object?>? ReadArguments(
		IReadOnlyDictionary<string, object> parameters,
		string name)
	{
		var value = parameters.GetValueOrDefault(name);
		var arguments = value switch
		{
			IReadOnlyDictionary<string, string> map => FromStringMap(map),
			IReadOnlyDictionary<string, object?> map => new Dictionary<string, object?>(map, StringComparer.Ordinal),
			JsonElement element => FromJson(element),
			string text => FromText(text),
			_ => null
		};

		return arguments is { Count: > 0 } ? arguments : null;
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

	private static Dictionary<string, object?>? FromText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(text);
			return FromJson(document.RootElement);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static Dictionary<string, object?>? FromJson(JsonElement element)
	{
		if (element.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var result = new Dictionary<string, object?>(StringComparer.Ordinal);
		foreach (var property in element.EnumerateObject())
		{
			result[property.Name] = property.Value.ValueKind switch
			{
				JsonValueKind.String => property.Value.GetString(),
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.Number => property.Value.TryGetInt64(out var integer)
					? integer
					: property.Value.GetDouble(),
				JsonValueKind.Null => null,
				_ => property.Value.GetRawText()
			};
		}

		return result;
	}
}
