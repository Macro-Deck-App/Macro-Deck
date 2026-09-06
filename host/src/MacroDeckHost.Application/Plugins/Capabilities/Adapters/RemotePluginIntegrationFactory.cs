using MacroDeckHost.Application.Plugins.Assets;
using MacroDeck.Localization;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters;

public static class RemotePluginIntegrationFactory
{
	public static RemotePluginIntegration Create(
		string pluginId,
		LocalizedText displayName,
		string version,
		RemotePluginCapabilitySnapshot snapshot,
		IPluginCapabilityInvoker invoker,
		IRemotePluginConnectionState connectionState,
		IPluginAssetCache assetCache,
		bool hasIcon,
		bool hasConfigFlow,
		bool hasDynamicEventOptions)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);
		ArgumentException.ThrowIfNullOrEmpty(version);
		ArgumentNullException.ThrowIfNull(snapshot);
		ArgumentNullException.ThrowIfNull(invoker);
		ArgumentNullException.ThrowIfNull(connectionState);
		ArgumentNullException.ThrowIfNull(assetCache);

		if (displayName.IsEmpty)
		{
			throw new ArgumentException("A display name is required.", nameof(displayName));
		}

		return (hasIcon, hasConfigFlow, hasDynamicEventOptions) switch
		{
			(false, false, false) => new RemotePluginIntegrationPlain(pluginId,
				displayName,
				version,
				snapshot,
				invoker,
				connectionState,
				assetCache),
			(true, false, false) => new RemotePluginIntegrationWithIcon(pluginId,
				displayName,
				version,
				snapshot,
				invoker,
				connectionState,
				assetCache),
			(false, true, false) => new RemotePluginIntegrationWithConfigFlow(pluginId,
				displayName,
				version,
				snapshot,
				invoker,
				connectionState,
				assetCache),
			(false, false, true) => new RemotePluginIntegrationWithDynamicEventOptions(pluginId,
				displayName,
				version,
				snapshot,
				invoker,
				connectionState,
				assetCache),
			(true, true, false) => new RemotePluginIntegrationWithIconAndConfigFlow(pluginId,
				displayName,
				version,
				snapshot,
				invoker,
				connectionState,
				assetCache),
			(true, false, true) => new RemotePluginIntegrationWithIconAndDynamicEventOptions(pluginId,
				displayName,
				version,
				snapshot,
				invoker,
				connectionState,
				assetCache),
			(false, true, true) => new RemotePluginIntegrationWithConfigFlowAndDynamicEventOptions(pluginId,
				displayName,
				version,
				snapshot,
				invoker,
				connectionState,
				assetCache),
			(true, true, true) => new RemotePluginIntegrationWithIconConfigFlowAndDynamicEventOptions(pluginId,
				displayName,
				version,
				snapshot,
				invoker,
				connectionState,
				assetCache)
		};
	}
}
