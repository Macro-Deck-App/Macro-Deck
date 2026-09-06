namespace MacroDeck.Localization;

/// <summary>Turns a localization reference into text for one culture.</summary>
public interface ILocalizationResolver
{
	/// <summary>Resolves <paramref name="value" /> in <paramref name="culture" />, walking the fallback
	/// chain. Never throws and never returns null: a key no culture carries resolves to
	/// <see cref="LocalizationDefaults.MissingText" />.</summary>
	string Resolve(LocalizedString value, string? culture);

	/// <summary>Resolves <paramref name="text" />: literal text is returned as written, a reference is
	/// resolved, and the absent value returns <c>null</c>.</summary>
	string? Resolve(LocalizedText text, string? culture);
}
