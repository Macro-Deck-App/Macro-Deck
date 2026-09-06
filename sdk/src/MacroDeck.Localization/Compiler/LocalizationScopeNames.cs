namespace MacroDeck.Localization.Compiler;

/// <summary>
/// The scope vocabulary, in the shared core so the source generator names a plugin's scope exactly the
/// way the runtime does. The public door onto these is <c>LocalizationScope</c>, which the generator
/// cannot use: it is a net10.0 type and a Roslyn component is netstandard2.0.
/// </summary>
internal static class LocalizationScopeNames
{
	/// <summary>The scope of the reusable catalog Macro Deck itself ships.</summary>
	public const string MacroDeck = "macrodeck";

	/// <summary>The prefix every plugin-owned scope starts with.</summary>
	public const string PluginPrefix = "plugin:";

	/// <summary>The prefix of a scope Macro Deck's own application owns, such as <c>macrodeck.app</c>.</summary>
	public const string MacroDeckPrefix = MacroDeck + ".";

	/// <summary>
	/// Whether <paramref name="scope" /> is one of Macro Deck's own application scopes - the catalogs
	/// the app ships but does not publish, as distinct from the reusable <see cref="MacroDeck" /> catalog
	/// plugins compile against.
	/// </summary>
	public static bool IsMacroDeckOwned(string? scope)
		=> scope != null &&
			scope.StartsWith(MacroDeckPrefix, StringComparison.Ordinal) &&
			scope.Length > MacroDeckPrefix.Length &&
			scope.IndexOf(':') < 0;
}
