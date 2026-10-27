using System.Net;
using System.Text.RegularExpressions;

namespace MacroDeckHost.Integrations.Feeds.Protocol;

internal static partial class FeedText
{
	public static string? ToPlainText(string? text)
	{
		if (string.IsNullOrWhiteSpace(text))
		{
			return null;
		}

		var withoutCode = StyleBlocks().Replace(ScriptBlocks().Replace(text, string.Empty), string.Empty);
		var withBreaks = LineBreakTags().Replace(withoutCode, "\n");
		var withoutTags = Tags().Replace(withBreaks, string.Empty);
		var decoded = WebUtility.HtmlDecode(withoutTags).Replace("\r\n", "\n", StringComparison.Ordinal);
		var collapsed = BlankLines().Replace(decoded, "\n\n").Trim();

		return collapsed.Length == 0 ? null : collapsed;
	}

	public static string? SingleLine(string? text)
	{
		if (ToPlainText(text) is not { } plain)
		{
			return null;
		}

		return Whitespace().Replace(plain, " ").Trim();
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

	public static string? ClipOptional(string? text, int maxLength)
	{
		return text is null ? null : Clip(text, maxLength);
	}

	public static string? FirstImageSource(string? html)
	{
		if (html is null)
		{
			return null;
		}

		var match = ImageSource().Match(html);
		return match.Success ? WebUtility.HtmlDecode(match.Groups["src"].Value) : null;
	}

	// Feed text is untrusted, so markup patterns use the linear engine. One script|style alternation is
	// quadratic even there, which is why script and style blocks are removed by separate patterns.
	[GeneratedRegex(@"<\s*script\b[^>]*>.*?<\s*/\s*script\s*>",
		RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking)]
	private static partial Regex ScriptBlocks();

	[GeneratedRegex(@"<\s*style\b[^>]*>.*?<\s*/\s*style\s*>",
		RegexOptions.IgnoreCase | RegexOptions.Singleline | RegexOptions.NonBacktracking)]
	private static partial Regex StyleBlocks();

	[GeneratedRegex(@"<\s*br\s*/?\s*>|<\s*/\s*(p|div|li|tr|h[1-6])\s*>",
		RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
	private static partial Regex LineBreakTags();

	[GeneratedRegex(@"<[^>]*>", RegexOptions.NonBacktracking)]
	private static partial Regex Tags();

	[GeneratedRegex(@"\n[ \t]*\n(?:[ \t]*\n)+")]
	private static partial Regex BlankLines();

	[GeneratedRegex(@"\s+")]
	private static partial Regex Whitespace();

	[GeneratedRegex("""<img\b[^>]*?\bsrc\s*=\s*["'](?<src>[^"']+)["']""",
		RegexOptions.IgnoreCase | RegexOptions.NonBacktracking)]
	private static partial Regex ImageSource();
}
