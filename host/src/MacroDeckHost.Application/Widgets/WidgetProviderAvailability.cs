using MacroDeck.Localization;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;

namespace MacroDeckHost.Application.Widgets;

public sealed record UnavailableWidgetProvider(string OwnerId, LocalizedText Name);

public interface IWidgetProviderAvailability
{
	UnavailableWidgetProvider? Check(string? widgetType);
}

public sealed class WidgetProviderAvailability : IWidgetProviderAvailability
{
	private readonly IWidgetTypeRegistry _widgetTypes;
	private readonly IIntegrationRegistry _integrations;
	private readonly IRemotePluginConnectionState _connections;

	public WidgetProviderAvailability(
		IWidgetTypeRegistry widgetTypes,
		IIntegrationRegistry integrations,
		IRemotePluginConnectionState connections)
	{
		_widgetTypes = widgetTypes;
		_integrations = integrations;
		_connections = connections;
	}

	public UnavailableWidgetProvider? Check(string? widgetType)
	{
		if (!QualifiedId.TryParse(widgetType, out var id) || id.OwnerId.Length == 0)
		{
			return null;
		}

		var owner = id.OwnerId;

		// IsEnabled is deliberately not consulted: it reads false for a connected config-flow plugin the
		// user never set up, and disabling a plugin does not stop it serving its widgets.
		var available = _widgetTypes.IsRegistered(widgetType) &&
			(_integrations.GetOrigin(owner) != IntegrationOrigin.Plugin || _connections.IsConnected(owner));

		if (available)
		{
			return null;
		}

		var name = _integrations.Integrations
			.FirstOrDefault(integration => string.Equals(integration.Id, owner, StringComparison.Ordinal))
			?.Name ?? LocalizedText.FromLiteral(owner);

		return new UnavailableWidgetProvider(owner, name);
	}
}
