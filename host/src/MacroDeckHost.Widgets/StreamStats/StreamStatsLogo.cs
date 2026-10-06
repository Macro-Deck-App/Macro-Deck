using MacroDeck.Sdk;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Widgets.StreamStats;

internal static class StreamStatsLogo
{
	public static UiResource? Register(
		StreamPlatform platform,
		IIntegrationRegistry? integrations,
		IUiResourceStore store)
	{
		ArgumentNullException.ThrowIfNull(platform);
		ArgumentNullException.ThrowIfNull(store);

		if (integrations?.Integrations.FirstOrDefault(integration => integration.Id == platform.OwnerId) is
			not IIntegrationIconProvider provider)
		{
			return null;
		}

		return store.Register(new UiResourceRegistration
		{
			OwnerId = platform.OwnerId,
			Name = "header-icon",
			MediaType = provider.IconMimeType,
			Content = provider.GetIcon(),
		});
	}
}
