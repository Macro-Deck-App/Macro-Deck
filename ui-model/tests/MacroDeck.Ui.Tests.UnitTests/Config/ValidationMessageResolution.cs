using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Ui.Config.Validation;

namespace MacroDeck.Ui.Tests.UnitTests.Config;

/// <summary>
/// Resolves a validation outcome's message the way a client does. Validation now produces a localization
/// reference rather than English text, so a test that wants to assert what the user reads has to resolve
/// it - against the real Macro Deck catalog, not a stub, since the catalog's own wording is part of what
/// these tests pin.
/// </summary>
internal static class ValidationMessageResolution
{
	private static readonly LocalizationResolver _resolver = BuildResolver();

	/// <summary>What a reader on <paramref name="culture" /> sees. Defaults to English.</summary>
	public static string? Resolved(this UiValidationOutcome outcome, string culture = "en")
		=> _resolver.Resolve(outcome.Message, culture);

	/// <summary>What a reader sees for a node property carrying text. The property is a localization
	/// reference on the wire, so this is the client's half of the same journey.</summary>
	public static string? ResolvedText(this JsonElement property, string culture = "en")
		=> _resolver.Resolve(JsonSerializer.Deserialize<LocalizedText>(property.GetRawText()), culture);

	private static LocalizationResolver BuildResolver()
	{
		var registry = new LocalizationCatalogRegistry();
		registry.Register(MacroDeckStrings.LocalizationCatalog);

		return new LocalizationResolver(registry);
	}
}
