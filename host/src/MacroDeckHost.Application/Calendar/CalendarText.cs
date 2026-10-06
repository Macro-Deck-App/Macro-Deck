using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Calendar;

public static partial class CalendarText
{
	public static string? ToPlainText(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		var withoutCode = CodeBlocks().Replace(text, string.Empty);
		var withBreaks = LineBreakTags().Replace(withoutCode, "\n");
		var withoutTags = Tags().Replace(withBreaks, string.Empty);
		var decoded = WebUtility.HtmlDecode(withoutTags).Replace("\r\n", "\n", StringComparison.Ordinal);
		var collapsed = BlankLines().Replace(decoded, "\n\n").Trim();

		return collapsed.Length == 0 ? null : collapsed;
	}

	public static string Clip(string? text, int maxLength)
	{
		if (text is null || maxLength <= 0)
		{
			return string.Empty;
		}

		if (text.Length <= maxLength)
		{
			return text;
		}

		var length = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
		return text[..length];
	}

	public static string? ClipOptional(string? text, int maxLength) => text is null ? null : Clip(text, maxLength);

	// Measured with the default JSON encoder the UI wire uses, which writes a non-ASCII character as six bytes.
	public static string ClipToJsonBytes(string text, int maxBytes)
	{
		if (JsonBytes(text) <= maxBytes)
		{
			return text;
		}

		var low = 0;
		var high = text.Length;

		while (low < high)
		{
			var middle = (low + high + 1) / 2;

			if (JsonBytes(Clip(text, middle)) <= maxBytes)
			{
				low = middle;
			}
			else
			{
				high = middle - 1;
			}
		}

		return Clip(text, low);
	}

	private static int JsonBytes(string text) => JsonEncodedText.Encode(text).EncodedUtf8Bytes.Length;

	[GeneratedRegex(@"<\s*(script|style)\b[^>]*>.*?<\s*/\s*\1\s*>", RegexOptions.IgnoreCase | RegexOptions.Singleline)]
	private static partial Regex CodeBlocks();

	[GeneratedRegex(@"<\s*br\s*/?\s*>|<\s*/\s*(p|div|li|tr|h[1-6])\s*>", RegexOptions.IgnoreCase)]
	private static partial Regex LineBreakTags();

	[GeneratedRegex(@"<[^>]*>")]
	private static partial Regex Tags();

	[GeneratedRegex(@"\n[ \t]*\n(?:[ \t]*\n)+")]
	private static partial Regex BlankLines();
}
