using System.Collections;
using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.AutoHotkey.Providers;

namespace MacroDeckHost.Integrations.AutoHotkey.Actions;

internal static class AutoHotkeyActionValues
{
	public static string? ReadText<T>(IReadOnlyDictionary<string, T> parameters, string name)
	{
		var text = (object?)parameters.GetValueOrDefault(name) switch
		{
			null => null,
			string value => value,
			JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
			JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
			JsonElement element => element.GetRawText(),
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			var other => other.ToString()
		};
		return string.IsNullOrWhiteSpace(text) ? null : text;
	}

	public static bool ReadBool<T>(IReadOnlyDictionary<string, T> parameters, string name, bool fallback)
	{
		return (object?)parameters.GetValueOrDefault(name) switch
		{
			bool value => value,
			JsonElement { ValueKind: JsonValueKind.True } => true,
			JsonElement { ValueKind: JsonValueKind.False } => false,
			string text when bool.TryParse(text, out var parsed) => parsed,
			JsonElement { ValueKind: JsonValueKind.String } element
				when bool.TryParse(element.GetString(), out var parsed) => parsed,
			_ => fallback
		};
	}

	public static TimeSpan ReadDuration<T>(IReadOnlyDictionary<string, T> parameters, string name, TimeSpan fallback)
	{
		double? milliseconds = (object?)parameters.GetValueOrDefault(name) switch
		{
			double value => value,
			float value => value,
			int value => value,
			long value => value,
			decimal value => (double)value,
			JsonElement { ValueKind: JsonValueKind.Number } element => element.GetDouble(),
			string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
				=> parsed,
			_ => null
		};

		return milliseconds is { } finite && double.IsFinite(finite) && finite > 0
			? TimeSpan.FromMilliseconds(finite)
			: fallback;
	}

	public static Guid? ReadGuid<T>(IReadOnlyDictionary<string, T> parameters, string name)
	{
		return Guid.TryParse(ReadText(parameters, name), out var id) ? id : null;
	}

	public static AutoHotkeyRunPolicy ReadPolicy<T>(IReadOnlyDictionary<string, T> parameters)
	{
		return ReadText(parameters, AutoHotkeyParameters.WhenRunning) switch
		{
			AutoHotkeyParameters.StartAnother => AutoHotkeyRunPolicy.StartAnother,
			AutoHotkeyParameters.Restart => AutoHotkeyRunPolicy.Restart,
			AutoHotkeyParameters.Fail => AutoHotkeyRunPolicy.Fail,
			_ => AutoHotkeyRunPolicy.Ignore
		};
	}

	public static IReadOnlyDictionary<string, string> ReadKeyValues<T>(
		IReadOnlyDictionary<string, T> parameters,
		string name)
	{
		var result = new Dictionary<string, string>(StringComparer.Ordinal);
		switch ((object?)parameters.GetValueOrDefault(name))
		{
			case JsonElement { ValueKind: JsonValueKind.Object } element:
				foreach (var property in element.EnumerateObject())
				{
					result[property.Name] = property.Value.ValueKind == JsonValueKind.String
						? property.Value.GetString() ?? string.Empty
						: property.Value.ToString();
				}

				break;
			case IEnumerable<KeyValuePair<string, string>> pairs:
				foreach (var (key, value) in pairs)
				{
					result[key] = value;
				}

				break;
			case IEnumerable<KeyValuePair<string, object?>> pairs:
				foreach (var (key, value) in pairs)
				{
					result[key] = Text(value);
				}

				break;
		}

		return result.Where(pair => pair.Key.Trim().Length > 0)
			.ToDictionary(pair => pair.Key.Trim(), pair => pair.Value, StringComparer.Ordinal);
	}

	public static IReadOnlyList<string> ReadStrings<T>(IReadOnlyDictionary<string, T> parameters, string name)
	{
		return (object?)parameters.GetValueOrDefault(name) switch
		{
			null => [],
			string single => [single],
			JsonElement { ValueKind: JsonValueKind.Array } element
				=> [.. element.EnumerateArray().Select(item => Text(item))],
			IEnumerable items => [.. items.Cast<object?>().Select(Text)],
			_ => []
		};
	}

	private static string Text(object? item)
	{
		return item switch
		{
			null => string.Empty,
			string text => text,
			JsonElement { ValueKind: JsonValueKind.String } element => element.GetString() ?? string.Empty,
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => item.ToString() ?? string.Empty
		};
	}
}
