using System.Runtime.CompilerServices;
using MacroDeck.Sdk;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Steam;
using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Widgets.Steam;

internal static class SteamWidgetIcons
{
	private static readonly ConditionalWeakTable<IUiResourceStore, Lazy<UiResource?>> _registrations = new();

	public static UiResource? Logo(IUiResourceStore store, IIntegrationRegistry integrations)
	{
		ArgumentNullException.ThrowIfNull(store);
		ArgumentNullException.ThrowIfNull(integrations);

		return _registrations.GetValue(store, registryStore => new Lazy<UiResource?>(() =>
			integrations.Integrations.FirstOrDefault(integration => integration.Id == SteamWidgetType.OwnerId) is
				IIntegrationIconProvider provider
				? registryStore.Register(new UiResourceRegistration
				{
					OwnerId = SteamWidgetType.OwnerId,
					Name = "logo",
					MediaType = provider.IconMimeType,
					Content = provider.GetIcon(),
				})
				: null)).Value;
	}
}
