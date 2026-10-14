using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace MacroDeck.Sdk.Variables;

/// <summary>
/// The rules for canonical variable names, the names a user types into a template: lowercase ASCII
/// letters, digits and underscores, starting with a letter (<c>^[a-z][a-z0-9_]*$</c>).
/// </summary>
public static partial class VariableNames
{
	private const string PublicPrefix = "vars.";

	/// <summary>
	/// Turns any text into a canonical name the way Macro Deck does: a leading <c>vars.</c> is dropped,
	/// accents are removed, letters are lowercased, every other character becomes an underscore, runs of
	/// underscores collapse and leading and trailing ones are trimmed, and a name starting with a digit gets
	/// a <c>v_</c> prefix. The result is empty when nothing usable is left.
	/// </summary>
	/// <remarks>The same input always gives the same name, on every Macro Deck version that has this
	/// member, so a name derived from stable text stays stable.</remarks>
	public static string Sanitize(string? input)
	{
		if (string.IsNullOrWhiteSpace(input))
		{
			return string.Empty;
		}

		var trimmed = input.Trim();
		if (trimmed.StartsWith(PublicPrefix, StringComparison.OrdinalIgnoreCase))
		{
			trimmed = trimmed[PublicPrefix.Length..];
		}

		var folded = trimmed.Normalize(NormalizationForm.FormD);
		var builder = new StringBuilder(folded.Length);
		foreach (var ch in folded)
		{
			if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
			{
				continue;
			}

			if (ch is >= 'a' and <= 'z' or >= '0' and <= '9' or '_')
			{
				builder.Append(ch);
			}
			else if (ch is >= 'A' and <= 'Z')
			{
				builder.Append(char.ToLowerInvariant(ch));
			}
			else
			{
				builder.Append('_');
			}
		}

		var collapsed = UnderscoreRuns().Replace(builder.ToString(), "_").Trim('_');

		if (collapsed.Length > 0 && char.IsDigit(collapsed[0]))
		{
			collapsed = "v_" + collapsed;
		}

		return collapsed;
	}

	/// <summary>Whether <paramref name="name" /> is already a canonical variable name.</summary>
	public static bool IsValid(string? name)
	{
		return !string.IsNullOrEmpty(name) && ValidName().IsMatch(name);
	}

	[GeneratedRegex("_+", RegexOptions.CultureInvariant)]
	private static partial Regex UnderscoreRuns();

	// \z rather than $: '$' also matches before a trailing newline.
	[GeneratedRegex(@"\A[a-z][a-z0-9_]*\z", RegexOptions.CultureInvariant)]
	private static partial Regex ValidName();
}
