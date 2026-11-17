using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Variables;

public static partial class TemplateClock
{
	// Stands for the current time wherever variable names are recorded or watched. A variable name can never
	// start with "@", so no real variable collides with it.
	public const string Name = "@clock";

	private const string DateNow = """\bdate\s*\.\s*(?:now|utc_now)\b""";

	// Widget data is stored JSON, and re-serializing it writes quotes as \u0027 and \u0022.
	private const string Quote = """(?:\\?["']|\\u002[27])""";

	private const string NowKeyword = """\s*(?:now|today)\s*""";

	public static bool Mentions(string? template)
	{
		return !string.IsNullOrEmpty(template) && ClockRead().IsMatch(template);
	}

	internal static bool IsNowKeyword(string? text)
	{
		var trimmed = text?.Trim();
		return string.Equals(trimmed, "now", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(trimmed, "today", StringComparison.OrdinalIgnoreCase);
	}

	[GeneratedRegex(DateNow + "|" + Quote + NowKeyword + Quote,
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex ClockRead();
}
