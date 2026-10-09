using System.Text.RegularExpressions;

namespace MacroDeckHost.Application.Icons;

public static partial class IconAppearanceTraits
{
	public const string ColorScheme = "colorScheme";

	public const string Motion = "motion";

	public const string Variant = "variant";

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

	public static bool TryGetVariantName(string? canonicalKey, out string name)
	{
		name = string.Empty;
		if (canonicalKey is null || !canonicalKey.StartsWith(Variant + "=", StringComparison.Ordinal) ||
			!TryParse(canonicalKey, out var traits) || traits.Count != 1)
		{
			return false;
		}

		name = Humanize(traits[Variant]);
		return true;
	}

	public static string Humanize(string token)
	{
		var words = WordBoundaryRegex().Split(token);
		var text = string.Join(' ', words.Select((word, index) =>
			index > 0 && word.Length > 1 && word.All(char.IsUpper) ? word : word.ToLowerInvariant()));
		return text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];
	}

	[GeneratedRegex("^[a-z][a-zA-Z0-9]{0,31}$")]
	private static partial Regex TokenRegex();

	[GeneratedRegex("(?<=[a-z0-9])(?=[A-Z])")]
	private static partial Regex WordBoundaryRegex();
}
