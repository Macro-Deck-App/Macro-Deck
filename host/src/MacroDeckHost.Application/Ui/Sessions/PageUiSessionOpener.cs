using System.Text.Json;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Capabilities.Adapters;
using MacroDeckHost.Application.Ui.Pages;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

namespace MacroDeckHost.Application.Ui.Sessions;

public interface IPageUiSessionOpener
{
	UiSessionOpenTicket Open(OpenPageUiSessionRequest request, string ownerPrincipal, bool isAdmin);
}

public sealed class PageUiSessionOpener : IPageUiSessionOpener
{
	private readonly IIntegrationRegistry _integrations;
	private readonly IRemotePluginSnapshotStore _snapshots;
	private readonly IUiSessionBroker _broker;
	private readonly IRemotePluginConnectionState _connections;
	private readonly IHostUiCulture? _culture;

	public PageUiSessionOpener(
		IIntegrationRegistry integrations,
		IRemotePluginSnapshotStore snapshots,
		IUiSessionBroker broker,
		IRemotePluginConnectionState connections,
		IHostUiCulture? culture = null)
	{
		_integrations = integrations;
		_snapshots = snapshots;
		_broker = broker;
		_connections = connections;
		_culture = culture;
	}

	public UiSessionOpenTicket Open(OpenPageUiSessionRequest request, string ownerPrincipal, bool isAdmin)
	{
		ArgumentNullException.ThrowIfNull(request);
		ownerPrincipal ??= string.Empty;

		// Checked first, so a device cannot learn which integrations declare which pages.
		if (!isAdmin)
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.SessionForbidden,
				"Only an admin session may open an integration page.");
		}

		var integration = _integrations.Integrations.FirstOrDefault(candidate =>
			string.Equals(candidate.Id, request.IntegrationId, StringComparison.Ordinal));

		if (integration is null
			|| !_integrations.IsEnabled(integration.Id)
			|| !PagesOf(integration).Any(page => string.Equals(page.Id, request.PageId, StringComparison.Ordinal)))
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.ProviderUnavailable,
				"That page does not exist.");
		}

		// Accepted, the open would fail before the client could attach, and a refused attach cannot say why.
		if (integration is RemotePluginIntegration && !_connections.IsConnected(integration.Id))
		{
			return UiSessionOpenTicket.Rejected(UiSessionErrorCodes.ProviderDisconnected,
				"The plugin serving that page is not connected.");
		}

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Page,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = BuildAttributes(integration.Id, request.PageId)
		};

		return _broker.Open(integration.Id, surface, ownerPrincipal);
	}

	private IReadOnlyList<UiPageDescriptor> PagesOf(IIntegration integration)
	{
		if (integration is RemotePluginIntegration)
		{
			return _snapshots.Has(integration.Id) ? _snapshots.GetSnapshot(integration.Id).UiPages : [];
		}

		return integration is IUiProvider provider ? UiPageCatalog.Of(provider) : [];
	}

	private Dictionary<string, JsonElement> BuildAttributes(string integrationId, string pageId)
	{
		var attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		{
			[UiPageSurfaceAttributes.IntegrationId] = JsonSerializer.SerializeToElement(integrationId),
			[UiPageSurfaceAttributes.PageId] = JsonSerializer.SerializeToElement(pageId)
		};

		if (_culture?.Current is { Length: > 0 } culture)
		{
			attributes[UiPageSurfaceAttributes.Culture] = JsonSerializer.SerializeToElement(culture);
		}

		return attributes;
	}
}
