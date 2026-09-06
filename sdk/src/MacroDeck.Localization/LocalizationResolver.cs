using MacroDeck.Localization.Compiler;

namespace MacroDeck.Localization;

/// <summary>Resolves references against whichever catalogs are registered at the moment of the call.</summary>
public sealed class LocalizationResolver : ILocalizationResolver
{
	private readonly ILocalizationCatalogRegistry _catalogs;

	/// <summary>Creates a resolver over <paramref name="catalogs" />.</summary>
	public LocalizationResolver(ILocalizationCatalogRegistry catalogs)
	{
		ArgumentNullException.ThrowIfNull(catalogs);
		_catalogs = catalogs;
	}

	// A localized argument may itself carry localized arguments - "{field} is required" where {field} is
	// a localized label. Bounded rather than unbounded: an argument graph is authored data, and a cycle
	// through a self-referencing catalog would otherwise not terminate.
	private const int MaxArgumentDepth = 4;

	/// <inheritdoc />
	public string Resolve(LocalizedString value, string? culture) => Resolve(value, culture, 0);

	private string Resolve(LocalizedString value, string? culture, int depth)
	{
		var catalog = _catalogs.Find(value.Key.Scope);

		if (catalog is not null)
		{
			foreach (var candidate in LocalizationCultureChain.For(culture, catalog.DefaultCulture))
			{
				// The plural probe sits inside the culture loop on purpose: probing it only after the
				// whole chain had failed could answer a German request with an English singular while a
				// German plural was sitting right there.
				if (catalog.TryGetTemplate(candidate, value.Key.Name, out var template) ||
					TryPluralTemplate(catalog, candidate, value, out template))
				{
					return LocalizationTemplate.Format(template, ResolveArguments(value.Arguments, culture, depth));
				}
			}
		}

		// A plugin whose catalog has not arrived yet, or a key removed by an update: the reference is
		// still renderable, and names what is missing rather than collapsing to an empty label.
		return LocalizationDefaults.MissingText(value.Key);
	}

	/// <summary>
	/// Looks a plural family up in <paramref name="candidate" />: a family is stored as its forms, so the
	/// base key the reference carries is never itself a template. Falls back to the <c>Other</c> form
	/// when the selected one is missing, which is what makes a family with only <c>Other</c> - the
	/// minimum the compiler enforces - resolve for every count.
	/// </summary>
	private static bool TryPluralTemplate(ILocalizationCatalog catalog,
		string candidate,
		LocalizedString value,
		out string template)
	{
		if (value.Arguments.TryGetValue(LocalizationPluralForms.CountParameter, out var count))
		{
			var name = value.Key.Name;

			if (catalog.TryGetTemplate(candidate,
					LocalizationPluralForms.FormKey(name, LocalizationPluralForms.FormOf(count)),
					out template) ||
				catalog.TryGetTemplate(candidate,
					LocalizationPluralForms.FormKey(name, LocalizationPluralForms.Other),
					out template))
			{
				return true;
			}
		}

		template = string.Empty;
		return false;
	}

	/// <inheritdoc />
	public string? Resolve(LocalizedText text, string? culture) => Resolve(text, culture, 0);

	private string? Resolve(LocalizedText text, string? culture, int depth)
		=> text.Localized is { } localized ? Resolve(localized, culture, depth) : text.Literal;

	private IReadOnlyDictionary<string, object?> ResolveArguments(
		IReadOnlyDictionary<string, object?> arguments,
		string? culture,
		int depth)
	{
		if (arguments.Count == 0 || depth >= MaxArgumentDepth)
		{
			return arguments;
		}

		Dictionary<string, object?>? resolved = null;

		foreach (var argument in arguments)
		{
			var replacement = argument.Value switch
			{
				LocalizedText text => Resolve(text, culture, depth + 1),
				LocalizedString reference => Resolve(reference, culture, depth + 1),
				_ => null,
			};

			if (replacement is null)
			{
				continue;
			}

			resolved ??= new Dictionary<string, object?>(arguments, StringComparer.Ordinal);
			resolved[argument.Key] = replacement;
		}

		return resolved ?? arguments;
	}
}
