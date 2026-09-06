using MacroDeckHost.Application.Plugins.Assets;
using MacroDeck.Sdk;
using MacroDeck.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters;

// The eight {icon} x {config-flow} x {dynamic-event-options} leaves. Each is a thin forward to the
// base class - see RemotePluginIntegration's remarks for why the interfaces cannot be merged into one
// class that implements everything unconditionally. CreateConfigFlow forwards to
// RemotePluginIntegration.CreateConfigFlowCore, which builds a real RemoteConfigFlow session. GetIcon
// reads Snapshot.IconBytes, which RemotePluginIntegrationRegistrar only ever hands to the "icon" leaf
// once the matching asset.commit has actually landed - see its remarks on the registration-time wait.

internal sealed class RemotePluginIntegrationPlain(
	string pluginId,
	LocalizedText displayName,
	string version,
	RemotePluginCapabilitySnapshot snapshot,
	IPluginCapabilityInvoker invoker,
	IRemotePluginConnectionState connectionState,
	IPluginAssetCache assetCache)
	: RemotePluginIntegration(pluginId, displayName, version, snapshot, invoker, connectionState, assetCache);

internal sealed class RemotePluginIntegrationWithIcon(
	string pluginId,
	LocalizedText displayName,
	string version,
	RemotePluginCapabilitySnapshot snapshot,
	IPluginCapabilityInvoker invoker,
	IRemotePluginConnectionState connectionState,
	IPluginAssetCache assetCache)
	: RemotePluginIntegration(pluginId, displayName, version, snapshot, invoker, connectionState, assetCache),
		IIntegrationIconProvider
{
	public string IconMimeType => Snapshot.IconMimeType;

	public byte[] GetIcon() => Snapshot.IconBytes;
}

internal sealed class RemotePluginIntegrationWithConfigFlow(
	string pluginId,
	LocalizedText displayName,
	string version,
	RemotePluginCapabilitySnapshot snapshot,
	IPluginCapabilityInvoker invoker,
	IRemotePluginConnectionState connectionState,
	IPluginAssetCache assetCache)
	: RemotePluginIntegration(pluginId, displayName, version, snapshot, invoker, connectionState, assetCache),
		IConfigFlowProvider
{
	public bool AllowsMultipleConfigurations => Snapshot.AllowsMultipleConfigurations;

	public IConfigFlow CreateConfigFlow() => CreateConfigFlowCore();
}

internal sealed class RemotePluginIntegrationWithDynamicEventOptions(
	string pluginId,
	LocalizedText displayName,
	string version,
	RemotePluginCapabilitySnapshot snapshot,
	IPluginCapabilityInvoker invoker,
	IRemotePluginConnectionState connectionState,
	IPluginAssetCache assetCache)
	: RemotePluginIntegration(pluginId, displayName, version, snapshot, invoker, connectionState, assetCache),
		IDynamicEventOptionsProvider
{
	public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context,
		CancellationToken cancellationToken)
		=> GetEventOptionsCoreAsync(context, cancellationToken);
}

internal sealed class RemotePluginIntegrationWithIconAndConfigFlow(
	string pluginId,
	LocalizedText displayName,
	string version,
	RemotePluginCapabilitySnapshot snapshot,
	IPluginCapabilityInvoker invoker,
	IRemotePluginConnectionState connectionState,
	IPluginAssetCache assetCache)
	: RemotePluginIntegration(pluginId, displayName, version, snapshot, invoker, connectionState, assetCache),
		IIntegrationIconProvider, IConfigFlowProvider
{
	public string IconMimeType => Snapshot.IconMimeType;

	public byte[] GetIcon() => Snapshot.IconBytes;

	public bool AllowsMultipleConfigurations => Snapshot.AllowsMultipleConfigurations;

	public IConfigFlow CreateConfigFlow() => CreateConfigFlowCore();
}

internal sealed class RemotePluginIntegrationWithIconAndDynamicEventOptions(
	string pluginId,
	LocalizedText displayName,
	string version,
	RemotePluginCapabilitySnapshot snapshot,
	IPluginCapabilityInvoker invoker,
	IRemotePluginConnectionState connectionState,
	IPluginAssetCache assetCache)
	: RemotePluginIntegration(pluginId, displayName, version, snapshot, invoker, connectionState, assetCache),
		IIntegrationIconProvider, IDynamicEventOptionsProvider
{
	public string IconMimeType => Snapshot.IconMimeType;

	public byte[] GetIcon() => Snapshot.IconBytes;

	public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context,
		CancellationToken cancellationToken)
		=> GetEventOptionsCoreAsync(context, cancellationToken);
}

internal sealed class RemotePluginIntegrationWithConfigFlowAndDynamicEventOptions(
	string pluginId,
	LocalizedText displayName,
	string version,
	RemotePluginCapabilitySnapshot snapshot,
	IPluginCapabilityInvoker invoker,
	IRemotePluginConnectionState connectionState,
	IPluginAssetCache assetCache)
	: RemotePluginIntegration(pluginId, displayName, version, snapshot, invoker, connectionState, assetCache),
		IConfigFlowProvider, IDynamicEventOptionsProvider
{
	public bool AllowsMultipleConfigurations => Snapshot.AllowsMultipleConfigurations;

	public IConfigFlow CreateConfigFlow() => CreateConfigFlowCore();

	public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context,
		CancellationToken cancellationToken)
		=> GetEventOptionsCoreAsync(context, cancellationToken);
}

internal sealed class RemotePluginIntegrationWithIconConfigFlowAndDynamicEventOptions(
	string pluginId,
	LocalizedText displayName,
	string version,
	RemotePluginCapabilitySnapshot snapshot,
	IPluginCapabilityInvoker invoker,
	IRemotePluginConnectionState connectionState,
	IPluginAssetCache assetCache)
	: RemotePluginIntegration(pluginId, displayName, version, snapshot, invoker, connectionState, assetCache),
		IIntegrationIconProvider, IConfigFlowProvider, IDynamicEventOptionsProvider
{
	public string IconMimeType => Snapshot.IconMimeType;

	public byte[] GetIcon() => Snapshot.IconBytes;

	public bool AllowsMultipleConfigurations => Snapshot.AllowsMultipleConfigurations;

	public IConfigFlow CreateConfigFlow() => CreateConfigFlowCore();

	public Task<DynamicOptionsResult> GetEventOptionsAsync(EventOptionsContext context,
		CancellationToken cancellationToken)
		=> GetEventOptionsCoreAsync(context, cancellationToken);
}
