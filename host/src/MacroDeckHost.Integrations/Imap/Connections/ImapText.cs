using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Imap.Connections;

internal static class ImapText
{
	private static readonly LocalizationResolver _resolver = CreateResolver();

	// A snapshot's status message is plain text, so it is resolved in the system language: the integration
	// is not told which language the user picked, just as an out-of-process plugin is not.
	public static string Resolve(LocalizedString value)
	{
		return _resolver.Resolve(value, CultureInfo.CurrentUICulture.Name);
	}

	private static LocalizationResolver CreateResolver()
	{
		var catalogs = new LocalizationCatalogRegistry();
		catalogs.Register(MacroDeckStrings.LocalizationCatalog);
		catalogs.Register(AppStrings.LocalizationCatalog);
		return new LocalizationResolver(catalogs);
	}
}
