namespace MacroDeck.Localization;

/// <summary>
/// Every catalog currently available, keyed by scope. A plugin's catalog appears when the plugin loads
/// and disappears when it unloads.
/// </summary>
public interface ILocalizationCatalogRegistry
{
	/// <summary>Adds <paramref name="catalog" />, replacing any catalog already registered for its scope.
	/// The replacement is atomic: a concurrent reader sees either the whole old catalog or the whole new
	/// one, never a mixture, which is what makes a plugin update safe while a client is rendering.</summary>
	void Register(ILocalizationCatalog catalog);

	/// <summary>Removes the catalog registered for <paramref name="scope" />, atomically.</summary>
	/// <returns><c>true</c> when a catalog was registered for that scope.</returns>
	bool Unregister(string scope);

	/// <summary>The catalog for <paramref name="scope" />, or <c>null</c> when none is registered.</summary>
	ILocalizationCatalog? Find(string scope);

	/// <summary>Every registered scope, as of the moment of the call.</summary>
	IReadOnlyList<string> Scopes { get; }
}
