using System.Collections.Concurrent;
using MacroDeck.Localization;
using MacroDeck.Plugin.Packaging.Manifest;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Identity;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Application.Plugins.Runtime;

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
	private readonly IPluginInstallationCatalog? _installationCatalog;
	private readonly IPluginManifestReader? _manifestReader;
	private readonly ConcurrentDictionary<string, LocalizedText> _lastKnownNames = new(StringComparer.Ordinal);

	public WidgetProviderAvailability(
		IWidgetTypeRegistry widgetTypes,
		IIntegrationRegistry integrations,
		IRemotePluginConnectionState connections)
		: this(widgetTypes, integrations, connections, null, null)
	{
	}

	public WidgetProviderAvailability(
		IWidgetTypeRegistry widgetTypes,
		IIntegrationRegistry integrations,
		IRemotePluginConnectionState connections,
		IPluginInstallationCatalog? installationCatalog,
		IPluginManifestReader? manifestReader)
	{
		_widgetTypes = widgetTypes;
		_integrations = integrations;
		_connections = connections;
		_installationCatalog = installationCatalog;
		_manifestReader = manifestReader;
	}

	public UnavailableWidgetProvider? Check(string? widgetType)
	{
		if (!QualifiedId.TryParse(widgetType, out var id) || id.OwnerId.Length == 0)
		{
			return null;
		}

		var owner = id.OwnerId;
		var integration = _integrations.Integrations
			.FirstOrDefault(candidate => string.Equals(candidate.Id, owner, StringComparison.Ordinal));
		if (integration is not null)
		{
			_lastKnownNames[owner] = integration.Name;
		}

		// IsEnabled is deliberately not consulted: it reads false for a connected config-flow plugin the
		// user never set up, and disabling a plugin does not stop it serving its widgets.
		var available = _widgetTypes.IsRegistered(widgetType)
			&& (_integrations.GetOrigin(owner) != IntegrationOrigin.Plugin || _connections.IsConnected(owner));

		if (available)
		{
			return null;
		}

		var name = ResolveName(owner, integration);

		return new UnavailableWidgetProvider(owner, name);
	}

	private LocalizedText ResolveName(string owner, IIntegration? integration)
	{
		if (integration is not null)
		{
			return integration.Name;
		}

		if (_installationCatalog is not null
			&& _manifestReader is not null
			&& _installationCatalog.Discover().FirstOrDefault(plugin =>
				string.Equals(plugin.PluginId, owner, StringComparison.Ordinal)) is { ActiveVersion: { } active } installed)
		{
			var result = _manifestReader.Read(active.ManifestPath, installed.PluginId, active.Version);
			if (result.Success && !string.IsNullOrWhiteSpace(result.Manifest?.Name))
			{
				return LocalizedText.FromLiteral(result.Manifest.Name);
			}
		}

		return _lastKnownNames.TryGetValue(owner, out var lastKnownName)
			? lastKnownName
			: LocalizedText.FromLiteral(owner);
	}
}
