using System.Globalization;
using System.Text.RegularExpressions;

namespace MacroDeck.Sdk.Colors;

// Mirrors the host's colour grammar so a plugin facing a Macro Deck without the colors host api still
// resolves fixed colours itself; the shared fixture vectors keep the two in step.
internal static partial class ColorGrammar
{
	public const int MaxReferenceLength = 1024;

	public const int MaxReferenceSteps = 32;

	public static bool IsReference(string? value)
	{
		if (value is null || value.Length > MaxReferenceLength)
		{
			return false;
		}

		var trimmed = value.Trim();
		if (!trimmed.StartsWith("{{", StringComparison.Ordinal) || !trimmed.EndsWith("}}", StringComparison.Ordinal))
		{
			return false;
		}

		var segments = trimmed[2..^2].Split('|');
		return segments.Length >= 2 &&
			segments.Length - 2 <= MaxReferenceSteps &&
			RootPattern().IsMatch(segments[0]) &&
			ColorFilterPattern().IsMatch(segments[1]) &&
			segments.Skip(2).All(segment => AmountStepPattern().IsMatch(segment) || MixStepPattern().IsMatch(segment));
	}

	public static string? CanonicalLiteral(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return null;
		}

		var text = value.Trim();
		return text[0] == '#' ? Hex(text[1..]) : Functional(text);
	}

	private static string? Hex(string hex)
	{
		if (hex.Length is not (3 or 4 or 6 or 8) || !hex.All(Uri.IsHexDigit))
		{
			return null;
		}

		if (hex.Length is 3 or 4)
		{
			hex = string.Concat(hex.Select(nibble => new string(nibble, 2)));
		}

		hex = hex.ToLowerInvariant();
		return hex.Length == 8 && hex.EndsWith("ff", StringComparison.Ordinal) ? "#" + hex[..6] : "#" + hex;
	}

	private static string? Functional(string text)
	{
		var match = FunctionalPattern().Match(text);
		if (!match.Success || (match.Groups["fn"].Value.Length == 4) != match.Groups["a"].Success)
		{
			return null;
		}

		if (!byte.TryParse(match.Groups["r"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var r) ||
			!byte.TryParse(match.Groups["g"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var g) ||
			!byte.TryParse(match.Groups["b"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out var b))
		{
			return null;
		}

		var a = 255;
		if (match.Groups["a"].Success)
		{
			if (!double.TryParse(match.Groups["a"].Value, NumberStyles.Float, CultureInfo.InvariantCulture,
					out var alpha) ||
				alpha is < 0 or > 1)
			{
				return null;
			}

			a = (int)Math.Floor((alpha * 255) + 0.5);
		}

		return a == 255
			? string.Create(CultureInfo.InvariantCulture, $"#{r:x2}{g:x2}{b:x2}")
			: string.Create(CultureInfo.InvariantCulture, $"#{r:x2}{g:x2}{b:x2}{a:x2}");
	}

	[GeneratedRegex(@"\A\s*vars\.[a-z][a-z0-9_]*\s*\z", RegexOptions.CultureInvariant)]
	private static partial Regex RootPattern();

	[GeneratedRegex(@"\A\s*color\s*\z", RegexOptions.CultureInvariant)]
	private static partial Regex ColorFilterPattern();

	[GeneratedRegex(
		@"\A\s*color_(?:lighten|darken|saturate|desaturate|opacity|increase_opacity|reduce_opacity|hue)\s*:\s*-?\d+(?:\.\d+)?\s*\z",
		RegexOptions.CultureInvariant)]
	private static partial Regex AmountStepPattern();

	[GeneratedRegex(
		"\\A\\s*color_mix\\s*:\\s*(?:vars\\.[a-z][a-z0-9_]*|\"#(?:[0-9a-fA-F]{8}|[0-9a-fA-F]{6}|[0-9a-fA-F]{3})\")\\s*,\\s*-?\\d+(?:\\.\\d+)?\\s*\\z",
		RegexOptions.CultureInvariant)]
	private static partial Regex MixStepPattern();

	[GeneratedRegex(@"\A(?<fn>rgba?)\(\s*(?<r>\d{1,3})\s*,\s*(?<g>\d{1,3})\s*,\s*(?<b>\d{1,3})\s*(?:,\s*(?<a>\d+(?:\.\d+)?|\.\d+)\s*)?\)\z",
		RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
	private static partial Regex FunctionalPattern();
}
