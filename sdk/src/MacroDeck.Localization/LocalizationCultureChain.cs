using MacroDeck.Localization.Compiler;

namespace MacroDeck.Localization;

/// <summary>The order cultures are tried in when resolving a key.</summary>
public static class LocalizationCultureChain
{
	/// <summary>
	/// Builds the fallback order for <paramref name="requested" />: the requested culture, then its
	/// neutral culture, then the catalog's own default language, then Macro Deck's default. Duplicates
	/// are collapsed and order is preserved, so <c>de-DE</c> against a catalog defaulting to <c>de</c>
	/// yields <c>de-DE, de, en</c>.
	/// </summary>
	/// <param name="requested">The culture the reader asked for. Null or blank starts at the default.</param>
	/// <param name="catalogDefaultCulture">The culture the catalog's own default resources are written in.</param>
	public static IReadOnlyList<string> For(string? requested, string? catalogDefaultCulture)
	{
		var chain = new List<string>(4);

		Add(chain, requested);
		Add(chain, LocalizationCultureName.NeutralOf(requested));
		Add(chain, catalogDefaultCulture);
		Add(chain, LocalizationDefaults.Culture);

		return chain;
	}

	private static void Add(List<string> chain, string? culture)
	{
		if (string.IsNullOrWhiteSpace(culture))
		{
			return;
		}

		foreach (var existing in chain)
		{
			if (string.Equals(existing, culture, StringComparison.OrdinalIgnoreCase))
			{
				return;
			}
		}

		chain.Add(culture!);
	}
}
