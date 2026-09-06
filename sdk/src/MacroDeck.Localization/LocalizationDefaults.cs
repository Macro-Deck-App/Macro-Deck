namespace MacroDeck.Localization;

/// <summary>The values the framework falls back to when nothing more specific applies.</summary>
public static class LocalizationDefaults
{
	/// <summary>Macro Deck's own default language - the last culture tried before a key is reported missing.</summary>
	public const string Culture = "en";

	/// <summary>Wraps a key that could not be resolved in any culture. Deliberately conspicuous: a blank
	/// label reads as a rendering bug, whereas <c>[[macrodeck:Common.Save]]</c> names the missing key and
	/// the scope that should have carried it.</summary>
	public static string MissingText(LocalizationKey key) => $"[[{key}]]";
}
