using System.Text.Json;
using MacroDeckHost.Application.Plugins.Assets;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.MusicPlayer;

public static class RemoteMusicPlayerFactory
{
	public static RemoteMusicPlayer Create(
		string pluginId,
		string instanceId,
		IPluginCapabilityInvoker invoker,
		IPluginAssetCache assetCache,
		bool hasCatalog,
		bool hasDevices,
		IReadOnlyDictionary<string, JsonElement>? options = null)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);
		ArgumentException.ThrowIfNullOrEmpty(instanceId);
		ArgumentNullException.ThrowIfNull(invoker);
		ArgumentNullException.ThrowIfNull(assetCache);

		return (hasCatalog, hasDevices) switch
		{
			(false, false) => new RemoteMusicPlayerPlain(pluginId, instanceId, invoker, assetCache) { Options = options },
			(true, false) => new RemoteMusicPlayerWithCatalog(pluginId, instanceId, invoker, assetCache) { Options = options },
			(false, true) => new RemoteMusicPlayerWithDevices(pluginId, instanceId, invoker, assetCache) { Options = options },
			(true, true) => new RemoteMusicPlayerWithCatalogAndDevices(pluginId, instanceId, invoker, assetCache) { Options = options }
		};
	}
}
