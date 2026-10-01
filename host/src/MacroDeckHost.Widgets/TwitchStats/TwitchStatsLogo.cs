using MacroDeck.Sdk;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Twitch.Stats;
using MacroDeckHost.Application.Ui.Resources;

namespace MacroDeckHost.Widgets.TwitchStats;

internal static class TwitchStatsLogo
{
	public static UiResource? Register(IIntegrationRegistry? integrations, IUiResourceStore store)
	{
		ArgumentNullException.ThrowIfNull(store);

		if (integrations?.Integrations.FirstOrDefault(integration => integration.Id == TwitchStatsWidgetType.OwnerId) is
			not IIntegrationIconProvider provider)
		{
			return null;
		}

		return store.Register(new UiResourceRegistration
		{
			OwnerId = TwitchStatsWidgetType.OwnerId,
			Name = "header-icon",
			MediaType = provider.IconMimeType,
			Content = provider.GetIcon(),
		});
	}
}
