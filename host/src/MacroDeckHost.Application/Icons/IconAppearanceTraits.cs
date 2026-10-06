using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Icons;

public static partial class IconAppearanceTraits
{
	public const string ColorScheme = "colorScheme";

	public const string Motion = "motion";

	public const string Light = "light";

	public const string Dark = "dark";

	public const string Static = "static";

	public const string Animated = "animated";

	public const int MaxTraits = 4;

	public const int MaxAppearancesPerIcon = 8;

	public static bool IsValid(IReadOnlyDictionary<string, string>? traits)
		=> traits is { Count: >= 1 and <= MaxTraits } &&
			traits.All(pair => TokenRegex().IsMatch(pair.Key) && TokenRegex().IsMatch(pair.Value));

	public static string ToKey(IReadOnlyDictionary<string, string> traits)
		=> string.Join(';',
			traits.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair => $"{pair.Key}={pair.Value}"));

	public static bool TryParse(string? canonicalKey, out IReadOnlyDictionary<string, string> traits)
	{
		traits = new Dictionary<string, string>(StringComparer.Ordinal);
		if (string.IsNullOrEmpty(canonicalKey))
		{
			return false;
		}

		var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var part in canonicalKey.Split(';'))
		{
			var separator = part.IndexOf('=', StringComparison.Ordinal);
			if (separator <= 0 || !parsed.TryAdd(part[..separator], part[(separator + 1)..]))
			{
				return false;
			}
		}

		if (!IsValid(parsed))
		{
			return false;
		}

		traits = parsed;
		return true;
	}

	[GeneratedRegex("^[a-z][a-zA-Z0-9]{0,31}$")]
	private static partial Regex TokenRegex();
}
