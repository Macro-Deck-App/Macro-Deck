using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Integrations.Steam.Widgets;

namespace MacroDeckHost.Integrations.Steam;

public sealed partial class SteamIntegration
{
	public IReadOnlyList<UiSurfaceDeclaration> Surfaces => SteamWidgetSessions.Surfaces;

	public Task<IUiSession?> CreateSessionAsync(UiSessionRequest request, CancellationToken cancellationToken)
	{
		var artwork = _artwork;

		return Task.FromResult(artwork is null
			? null
			: SteamWidgetSessions.Create(request, this, artwork, _logo, _time));
	}

	private static async Task<UiResource?> LogoAsync(IUiResourceRegistry resources)
	{
		try
		{
			return await resources.GetPluginIconAsync(SteamWidgetSessions.IconFolder, SteamWidgetSessions.LogoIcon);
		}
		catch (UiResourceException exception)
		{
			_logger.Debug(exception, "The Steam logo could not be registered");
			return null;
		}
	}
}
