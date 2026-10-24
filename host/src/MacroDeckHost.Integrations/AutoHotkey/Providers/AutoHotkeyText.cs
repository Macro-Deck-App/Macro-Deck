using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.AutoHotkey.Providers;

internal static class AutoHotkeyText
{
	private static readonly LocalizationResolver _resolver = CreateResolver();

	// Text that leaves as a plain string (variable values, answers to a script) is resolved in the system
	// language: an integration is not told which language the user picked, just as a plugin is not.
	public static string Resolve(LocalizedText value)
	{
		return _resolver.Resolve(value, CultureInfo.CurrentUICulture.Name) ?? string.Empty;
	}

	public static string Resolve(LocalizedText value, string? culture)
	{
		return _resolver.Resolve(value, culture ?? CultureInfo.CurrentUICulture.Name) ?? string.Empty;
	}

	private static LocalizationResolver CreateResolver()
	{
		var catalogs = new LocalizationCatalogRegistry();
		catalogs.Register(MacroDeckStrings.LocalizationCatalog);
		catalogs.Register(AppStrings.LocalizationCatalog);
		return new LocalizationResolver(catalogs);
	}
}
