using System.Collections.Concurrent;
using MacroDeckHost.Application.Plugins.Assets;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.Actions;

/// <summary>
/// Resolves a remote plugin's icon-provider action by <c>(pluginId, localId)</c>, standing in for the
/// ninth leaf <c>RemoteActionDefinitionFactory</c> deliberately does not grow. ADR 0056 (Consequences)
/// already priced this: "the next optional action interface should get its own registry and adapter the
/// way ADR 0050 split <c>ui</c> out, rather than doubling again." This is that registry, modelled
/// directly on <c>MacroDeckHost.Application.Plugins.Capabilities.Adapters.Ui.RemoteUiProviderRegistry</c>.
///
/// <para>
/// Unlike <c>ui</c>, which addresses a whole plugin by one <c>provider</c> local id, an icon provider is
/// one action among many a plugin declares, so resolution needs both halves of the pair. A consumer asks
/// this registry only when it already knows the action is a remote one and its descriptor said
/// <see cref="RemoteActionDescriptor.ProvidesIcon" /> - an in-process action that implements
/// <c>IIconProviderActionDefinition</c> is reached directly, with no adapter needed.
/// </para>
/// </summary>
public sealed class RemoteIconProviderActionRegistry
{
	private readonly IRemotePluginSnapshotStore _snapshots;
	private readonly IPluginCapabilityInvoker _invoker;
	private readonly IPluginAssetCache _assetCache;

	private readonly ConcurrentDictionary<(string PluginId, string LocalId), RemoteIconProviderAction>
		_adapters = new();

	public RemoteIconProviderActionRegistry(
		IRemotePluginSnapshotStore snapshots,
		IPluginCapabilityInvoker invoker,
		IPluginAssetCache assetCache)
	{
		_snapshots = snapshots;
		_invoker = invoker;
		_assetCache = assetCache;
	}

	public RemoteIconProviderAction? Resolve(string pluginId, string localId)
	{
		if (!_snapshots.Has(pluginId))
		{
			return null;
		}

		var providesIcon = _snapshots.GetSnapshot(pluginId).Actions
			.Any(action => string.Equals(action.LocalId, localId, StringComparison.Ordinal) && action.ProvidesIcon);

		if (!providesIcon)
		{
			return null;
		}

		return _adapters.GetOrAdd((pluginId, localId),
			static (key, state) =>
				new RemoteIconProviderAction(key.PluginId, key.LocalId, state.Invoker, state.AssetCache),
			(Invoker: _invoker, AssetCache: _assetCache));
	}
}
