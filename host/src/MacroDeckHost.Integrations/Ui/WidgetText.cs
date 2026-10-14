using MacroDeck.Localization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Widgets;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Integrations.Ui;

internal static class WidgetText
{
	private static readonly LocalizationResolver _resolver = CreateResolver();

	public static string Resolve(LocalizedString value, string? culture)
	{
		return _resolver.Resolve(value, culture);
	}

	public static string Resolve(LocalizedString value, UiSurface surface)
	{
		return Resolve(value, UiWidgetSurface.Culture(surface));
	}

	private static LocalizationResolver CreateResolver()
	{
		var catalogs = new LocalizationCatalogRegistry();
		catalogs.Register(MacroDeckStrings.LocalizationCatalog);
		catalogs.Register(AppStrings.LocalizationCatalog);
		return new LocalizationResolver(catalogs);
	}
}
