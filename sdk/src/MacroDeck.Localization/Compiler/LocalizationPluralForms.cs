using System.Globalization;

namespace MacroDeck.Localization.Compiler;

/// <summary>
/// The plural forms a resource set may declare, and the rule that picks one for a count. Shared by the
/// source generator, the runtime resolver and - mirrored line for line - the TypeScript and Rust
/// formatters, so every renderer picks the same form for the same number.
/// </summary>
/// <remarks>
/// <para>
/// Only <c>One</c> and <c>Other</c> exist, and the rule is <c>count == 1</c> for every culture. That is
/// deliberately not CLDR: .NET ships no plural-rule data, so a CLDR implementation here would have to be
/// hand-maintained and kept byte-identical against <c>Intl.PluralRules</c> in the browser and a third
/// implementation in the bootstrapper. Three hand-written copies of a large rule set is a worse failure
/// mode than a small one that is obviously the same everywhere. It is exactly correct for English,
/// German, Italian, Spanish and French, which all select on <c>n == 1</c>. Czech and Polish do not - both
/// need <c>few</c>/<c>many</c> forms this model has no room for - so their <c>Other</c> translations are a
/// deliberate approximation: phrased to avoid noun-count agreement (a count-agnostic label rather than a
/// declined noun) so the same text stays grammatical across every count, instead of only being correct for
/// one CLDR bucket. A language that cannot be phrased this way needs this model extended first, which is
/// why the form names are a closed set checked at build time rather than free text.
/// </para>
/// </remarks>
internal static class LocalizationPluralForms
{
	/// <summary>The form used when the count selects the singular.</summary>
	public const string One = "One";

	/// <summary>The form every other count selects, and the one a family must always carry.</summary>
	public const string Other = "Other";

	/// <summary>The placeholder a plural family's count is passed as.</summary>
	public const string CountParameter = "count";

	/// <summary>The marker declaring an entry to be one form of a plural family.</summary>
	public const string Marker = "plural";

	/// <summary>Whether <paramref name="segment" /> names a plural form.</summary>
	public static bool IsForm(string? segment)
		=> string.Equals(segment, One, StringComparison.Ordinal) ||
			string.Equals(segment, Other, StringComparison.Ordinal);

	/// <summary>The form <paramref name="count" /> selects.</summary>
	public static string FormOf(object? count) => IsOne(count) ? One : Other;

	/// <summary>The key one form of <paramref name="baseKey" /> is stored under.</summary>
	public static string FormKey(string baseKey, string form) => baseKey + "." + form;

	/// <summary>Splits a form key into its family and its form, or returns <c>false</c> when it is not one.</summary>
	public static bool TrySplit(string key, out string baseKey, out string form)
	{
		var separator = key.LastIndexOf('.');

		if (separator > 0)
		{
			var candidate = key.Substring(separator + 1);

			if (IsForm(candidate))
			{
				baseKey = key.Substring(0, separator);
				form = candidate;
				return true;
			}
		}

		baseKey = key;
		form = Other;
		return false;
	}

	/// <summary>The forms a family is checked for, in the order the generated documentation lists them.</summary>
	public static IReadOnlyList<string> All { get; } = new[] { One, Other };

	private static bool IsOne(object? count)
	{
		switch (count)
		{
			case null:
				return false;
			case int value:
				return value == 1;
			case long value:
				return value == 1;
			case double value:
				return value is 1d;
			case float value:
				return value is 1f;
			case decimal value:
				return value == 1m;
			case string text:
				return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
					parsed is 1d;
			case IConvertible convertible:
				try
				{
					return convertible.ToDouble(CultureInfo.InvariantCulture) is 1d;
				}
				catch (Exception exception) when (exception is FormatException
					or InvalidCastException
					or OverflowException)
				{
					return false;
				}
			default:
				return false;
		}
	}
}
