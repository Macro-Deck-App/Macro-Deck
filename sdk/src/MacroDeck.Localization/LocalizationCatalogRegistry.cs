namespace MacroDeck.Localization;

/// <summary>
/// The default <see cref="ILocalizationCatalogRegistry" />. Readers take a reference to an immutable
/// snapshot and writers swap a whole new one in, so registration and removal are atomic without any
/// reader ever taking a lock.
/// </summary>
public sealed class LocalizationCatalogRegistry : ILocalizationCatalogRegistry
{
	private readonly Lock _writeGate = new();

	private volatile Dictionary<string, ILocalizationCatalog> _catalogs =
		new(StringComparer.Ordinal);

	/// <inheritdoc />
	public IReadOnlyList<string> Scopes => [.. _catalogs.Keys];

	/// <inheritdoc />
	public void Register(ILocalizationCatalog catalog)
	{
		ArgumentNullException.ThrowIfNull(catalog);

		lock (_writeGate)
		{
			var replacement = new Dictionary<string, ILocalizationCatalog>(_catalogs, StringComparer.Ordinal)
			{
				[catalog.Scope] = catalog,
			};

			_catalogs = replacement;
		}
	}

	/// <inheritdoc />
	public bool Unregister(string scope)
	{
		if (string.IsNullOrEmpty(scope))
		{
			return false;
		}

		lock (_writeGate)
		{
			if (!_catalogs.ContainsKey(scope))
			{
				return false;
			}

			var replacement = new Dictionary<string, ILocalizationCatalog>(_catalogs, StringComparer.Ordinal);
			replacement.Remove(scope);
			_catalogs = replacement;
			return true;
		}
	}

	/// <inheritdoc />
	public ILocalizationCatalog? Find(string scope)
		=> scope is not null && _catalogs.TryGetValue(scope, out var catalog) ? catalog : null;
}
