using System.Globalization;

namespace MacroDeckHost.Integrations.YouTube.Actions;

internal static class YouTubeActionValues
{
	public static string? ReadText(IReadOnlyDictionary<string, object> parameters, string name)
	{
		var value = parameters.GetValueOrDefault(name);
		var text = value switch
		{
			null => null,
			string s => s,
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			_ => value.ToString()
		};

		return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
	}

	public static string ReadRawText(IReadOnlyDictionary<string, object> parameters, string name)
		=> parameters.GetValueOrDefault(name) switch
		{
			string s => s,
			IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
			{ } other => other.ToString() ?? string.Empty,
			null => string.Empty
		};

	public static int? ReadInt(IReadOnlyDictionary<string, object> parameters, string name)
		=> int.TryParse(ReadText(parameters, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: null;

	public static IReadOnlyList<string> ReadTags(IReadOnlyDictionary<string, object> parameters, string name)
		=> ReadText(parameters, name)
				?.Split([',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) ??
			[];
}
