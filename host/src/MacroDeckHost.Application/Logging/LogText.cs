using System.Globalization;
using System.Text;

namespace MacroDeckHost.Application.Logging;

internal static class LogText
{
	private const char LineSeparator = (char)0x2028;

	private const char ParagraphSeparator = (char)0x2029;

	public static string? Neutralize(string? value)
	{
		if (value is null)
		{
			return null;
		}

		var text = value
			.Replace("\r\n", "\\n", StringComparison.Ordinal)
			.Replace("\r", "\\n", StringComparison.Ordinal)
			.Replace("\n", "\\n", StringComparison.Ordinal);

		return EscapeControls(text, keepLineBreaks: false);
	}

	public static string? NeutralizeControls(string? value)
		=> value is null ? null : EscapeControls(value, keepLineBreaks: true);

	private static string EscapeControls(string text, bool keepLineBreaks)
	{
		StringBuilder? builder = null;
		for (var i = 0; i < text.Length; i++)
		{
			var character = text[i];
			if (!NeedsEscape(text, i, keepLineBreaks))
			{
				builder?.Append(character);
				continue;
			}

			builder ??= new StringBuilder(text.Length + 16).Append(text, 0, i);
			builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
		}

		return builder?.ToString() ?? text;
	}

	private static bool NeedsEscape(string text, int index, bool keepLineBreaks)
	{
		var character = text[index];
		switch (character)
		{
			case '\t':
				return false;
			case '\n':
				return !keepLineBreaks;
			case '\r':
				return !keepLineBreaks || index + 1 >= text.Length || text[index + 1] != '\n';
			default:
				return char.IsControl(character) || character is LineSeparator or ParagraphSeparator;
		}
	}
}
