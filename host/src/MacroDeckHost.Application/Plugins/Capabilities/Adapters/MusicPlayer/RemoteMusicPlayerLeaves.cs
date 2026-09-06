using MacroDeckHost.Application.Plugins.Assets;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.MusicPlayer;

internal sealed class RemoteMusicPlayerPlain(
	string pluginId,
	string instanceId,
	IPluginCapabilityInvoker invoker,
	IPluginAssetCache assetCache)
	: RemoteMusicPlayer(pluginId, instanceId, invoker, assetCache);

internal sealed class RemoteMusicPlayerWithCatalog(
	string pluginId,
	string instanceId,
	IPluginCapabilityInvoker invoker,
	IPluginAssetCache assetCache)
	: RemoteMusicPlayer(pluginId, instanceId, invoker, assetCache), ICatalogMusicPlayer
{
	public Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
		=> GetCatalogCoreAsync(kind, filter, cancellationToken);

	public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
		=> PlayItemCoreAsync(item, cancellationToken);
}

internal sealed class RemoteMusicPlayerWithDevices(
	string pluginId,
	string instanceId,
	IPluginCapabilityInvoker invoker,
	IPluginAssetCache assetCache)
	: RemoteMusicPlayer(pluginId, instanceId, invoker, assetCache), IMusicPlayerDeviceProvider
{
	public Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken)
		=> GetDevicesCoreAsync(cancellationToken);

	public Task TransferPlaybackAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken)
		=> TransferPlaybackCoreAsync(deviceId, startPlayback, cancellationToken);
}

internal sealed class RemoteMusicPlayerWithCatalogAndDevices(
	string pluginId,
	string instanceId,
	IPluginCapabilityInvoker invoker,
	IPluginAssetCache assetCache)
	: RemoteMusicPlayer(pluginId, instanceId, invoker, assetCache), ICatalogMusicPlayer,
		IMusicPlayerDeviceProvider
{
	public Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogAsync(
		string instanceId,
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
		=> GetCatalogCoreAsync(kind, filter, cancellationToken);

	public Task PlayItemAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
		=> PlayItemCoreAsync(item, cancellationToken);

	public Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken)
		=> GetDevicesCoreAsync(cancellationToken);

	public Task TransferPlaybackAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken)
		=> TransferPlaybackCoreAsync(deviceId, startPlayback, cancellationToken);
}
