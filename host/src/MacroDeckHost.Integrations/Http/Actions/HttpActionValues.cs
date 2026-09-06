using System.Globalization;
using System.Text.Json;

namespace MacroDeckHost.Integrations.Http.Actions;

internal static class HttpActionValues
{
	public static string? ReadText(IReadOnlyDictionary<string, object> parameters, string name)
	{
		var text = Stringify(parameters.GetValueOrDefault(name));
		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}

	public static string? ReadRawText(IReadOnlyDictionary<string, object> parameters, string name)
	{
		var text = Stringify(parameters.GetValueOrDefault(name));
		return text.Length == 0 ? null : text;
	}

	public static string ReadMethod(IReadOnlyDictionary<string, object> parameters, string name)
	{
		var raw = ReadText(parameters, name);
		return raw is null ? "GET" : raw.Trim().ToUpperInvariant();
	}

	public static bool ReadBool(IReadOnlyDictionary<string, object> parameters, string name, bool fallback)
		=> parameters.GetValueOrDefault(name) switch
		{
			bool b => b,
			string s when bool.TryParse(s, out var parsed) => parsed,
			_ => fallback
		};

	public static double ReadNumber(IReadOnlyDictionary<string, object> parameters, string name, double fallback)
		=> parameters.GetValueOrDefault(name) switch
		{
			long l => l,
			double d => d,
			string s when double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) =>
				parsed,
			_ => fallback
		};

	public static double ReadDurationMilliseconds(
		IReadOnlyDictionary<string, object> parameters,
		string name,
		double fallback)
		=> parameters.GetValueOrDefault(name) switch
		{
			long l => l,
			double d => d,
			string s when TryParseDurationMilliseconds(s, out var milliseconds) => milliseconds,
			_ => fallback
		};

	public static IReadOnlyDictionary<string, string> ReadKeyValue(
		IReadOnlyDictionary<string, object> parameters,
		string name)
	{
		var map = parameters.GetValueOrDefault(name) switch
		{
			IReadOnlyDictionary<string, string> dictionary => Clean(dictionary),
			IReadOnlyDictionary<string, object?> dictionary => Clean(dictionary),
			JsonElement element => FromJson(element),
			string text => FromText(text),
			_ => null
		};

		return map ?? new Dictionary<string, string>(StringComparer.Ordinal);
	}

	private static bool TryParseDurationMilliseconds(string value, out double milliseconds)
	{
		var trimmed = value.Trim();
		if (double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var plain))
		{
			milliseconds = plain;
			return true;
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
			milliseconds = number * factor;
			return true;
		}

		milliseconds = 0;
		return false;
	}

	private static Dictionary<string, string> Clean(IReadOnlyDictionary<string, string> map)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (key, value) in map)
		{
			if (!string.IsNullOrWhiteSpace(key))
			{
				result[key] = value;
			}
		}

		return result;
	}

	private static Dictionary<string, string> Clean(IReadOnlyDictionary<string, object?> map)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (key, value) in map)
		{
			if (!string.IsNullOrWhiteSpace(key))
			{
				result[key] = Stringify(value);
			}
		}

		return result;
	}

	private static Dictionary<string, string>? FromText(string text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		try
		{
			using var document = JsonDocument.Parse(text);
			return document.RootElement.ValueKind == JsonValueKind.Object ? FromJson(document.RootElement) : null;
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private static Dictionary<string, string> FromJson(JsonElement element)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		if (element.ValueKind != JsonValueKind.Object)
		{
			return result;
		}

		foreach (var property in element.EnumerateObject())
		{
			if (!string.IsNullOrWhiteSpace(property.Name))
			{
				result[property.Name] = Stringify(property.Value);
			}
		}

		return result;
	}

	private static string Stringify(object? value) => value switch
	{
		null => string.Empty,
		string s => s,
		bool b => b ? "true" : "false",
		IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
		_ => value.ToString() ?? string.Empty
	};

	private static string Stringify(JsonElement value) => value.ValueKind switch
	{
		JsonValueKind.String => value.GetString() ?? string.Empty,
		JsonValueKind.True => "true",
		JsonValueKind.False => "false",
		JsonValueKind.Null => string.Empty,
		_ => value.GetRawText()
	};
}
