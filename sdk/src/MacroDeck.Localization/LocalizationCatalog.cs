namespace MacroDeck.Localization;

/// <summary>An immutable <see cref="ILocalizationCatalog" /> built from already-validated resources.</summary>
public sealed class LocalizationCatalog : ILocalizationCatalog
{
	private readonly Dictionary<string, IReadOnlyDictionary<string, string>> _byCulture;

	/// <summary>Builds a catalog.</summary>
	/// <param name="scope">The owning scope.</param>
	/// <param name="defaultCulture">The culture the default-language resources are written in.</param>
	/// <param name="templatesByCulture">Culture to key to template. Copied defensively.</param>
	/// <exception cref="ArgumentException"><paramref name="scope" /> is not a valid scope.</exception>
	public LocalizationCatalog(string scope,
		string defaultCulture,
		IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> templatesByCulture)
	{
		if (!LocalizationScope.IsValid(scope))
		{
			throw new ArgumentException($"'{scope}' is not a valid localization scope.", nameof(scope));
		}

		ArgumentException.ThrowIfNullOrWhiteSpace(defaultCulture);
		ArgumentNullException.ThrowIfNull(templatesByCulture);

		Scope = scope;
		DefaultCulture = defaultCulture;

		var copy = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

		foreach (var culture in templatesByCulture)
		{
			copy[culture.Key] = new Dictionary<string, string>(culture.Value, StringComparer.Ordinal);
		}

		_byCulture = copy;
		Cultures = [.. copy.Keys];
	}

	/// <inheritdoc />
	public string Scope { get; }

	/// <inheritdoc />
	public string DefaultCulture { get; }

	/// <inheritdoc />
	public IReadOnlyList<string> Cultures { get; }

	/// <inheritdoc />
	public IReadOnlyCollection<string> KeysOf(string culture)
		=> culture is not null && _byCulture.TryGetValue(culture, out var templates)
			? (IReadOnlyCollection<string>)templates.Keys
			: [];

	/// <inheritdoc />
	public bool TryGetTemplate(string culture, string key, out string text)
	{
		text = string.Empty;

		return culture is not null &&
			key is not null &&
			_byCulture.TryGetValue(culture, out var templates) &&
			templates.TryGetValue(key, out text!);
	}
}
