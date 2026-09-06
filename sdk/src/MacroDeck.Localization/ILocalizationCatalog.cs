namespace MacroDeck.Localization;

/// <summary>The resources of one scope, in every culture that scope ships.</summary>
public interface ILocalizationCatalog
{
	/// <summary>The scope these resources belong to, from <see cref="LocalizationScope" />.</summary>
	string Scope { get; }

	/// <summary>The culture the scope's default-language resources are written in.</summary>
	string DefaultCulture { get; }

	/// <summary>Every culture this catalog can serve, <see cref="DefaultCulture" /> included.</summary>
	IReadOnlyList<string> Cultures { get; }

	/// <summary>Looks up one key in exactly one culture, with no fallback of its own - walking the
	/// fallback chain is <see cref="ILocalizationResolver" />'s job.</summary>
	/// <returns><c>true</c> when this culture carries the key.</returns>
	bool TryGetTemplate(string culture, string key, out string text);

	/// <summary>Every key <paramref name="culture" /> carries, or empty when it carries none. Needed to
	/// hand a whole culture over the wire; a lookup alone cannot be enumerated.</summary>
	IReadOnlyCollection<string> KeysOf(string culture);
}
