using System.Collections;
using System.Globalization;
using System.Text.Json;

namespace MacroDeckHost.Integrations.Clipboard.Actions;

internal static class ClipboardActionValues
{
	public static string? ReadString(IReadOnlyDictionary<string, object> parameters, string name)
	{
		return parameters.GetValueOrDefault(name) switch
		{
			null => null,
			string text => text,
			JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
			JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => null,
			JsonElement element => element.GetRawText(),
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			var other => other.ToString()
		};
	}

	public static IReadOnlyList<string> ReadStrings(IReadOnlyDictionary<string, object> parameters, string name)
	{
		return parameters.GetValueOrDefault(name) switch
		{
			null => [],
			string text => [text],
			JsonElement { ValueKind: JsonValueKind.Array } element =>
				[.. element.EnumerateArray().Select(item => Text(item))],
			JsonElement { ValueKind: JsonValueKind.String } element => [element.GetString() ?? string.Empty],
			JsonElement => [],
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
			JsonElement { ValueKind: JsonValueKind.Null or JsonValueKind.Undefined } => string.Empty,
			JsonElement element => element.GetRawText(),
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => item.ToString() ?? string.Empty
		};
	}
}
