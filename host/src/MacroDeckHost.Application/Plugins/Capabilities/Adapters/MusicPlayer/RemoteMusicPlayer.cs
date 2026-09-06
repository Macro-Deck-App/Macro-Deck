using System.Text.Json;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Capabilities.Mapping;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeckHost.Application.Plugins.Capabilities.Adapters.MusicPlayer;

public abstract class RemoteMusicPlayer(
	string pluginId,
	string instanceId,
	IPluginCapabilityInvoker invoker,
	IPluginAssetCache assetCache)
	: IMusicPlayer
{
	protected IPluginCapabilityInvoker Invoker { get; } = invoker;

	private readonly IPluginAssetCache _assetCache = assetCache;

	protected string PluginId { get; } = pluginId;

	protected string InstanceId { get; } = instanceId;

	public async Task<MusicPlayerState> GetStateAsync(CancellationToken cancellationToken = default)
	{
		try
		{
			var data = await Invoker.InvokeAsync(PluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.MusicPlayer,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.MusicPlayer.State,
						Arguments = new MusicPlayerInstanceArguments { InstanceId = InstanceId }
					},
					cancellationToken)
				.ConfigureAwait(false);

			var dto = data?.Deserialize<MusicPlayerStateDto>(PluginProtocolJson.Options);
			return dto is null ? MusicPlayerState.Unavailable() : MusicPlayerStateMapper.ToDomain(dto);
		}
		catch (RemoteCapabilityException exception)
		{
			return MusicPlayerState.Unavailable(exception.Message);
		}
	}

	public async Task<MusicPlayerArtwork?> GetArtworkAsync(string artworkId,
		CancellationToken cancellationToken = default)
	{
		try
		{
			var data = await Invoker.InvokeAsync(PluginId,
					new CapabilityInvokeRequest
					{
						Kind = CapabilityKinds.MusicPlayer,
						LocalId = ProviderCapabilityId.LocalId,
						Operation = CapabilityOperations.MusicPlayer.Artwork,
						Arguments = new MusicPlayerArtworkArguments { InstanceId = InstanceId, ArtworkId = artworkId }
					},
					cancellationToken)
				.ConfigureAwait(false);

			var dto = data?.Deserialize<MusicPlayerArtworkResult>(PluginProtocolJson.Options);
			if (dto is null)
			{
				return null;
			}

			if (dto.Data is { } inline)
			{
				return new MusicPlayerArtwork(Convert.FromBase64String(inline), dto.MimeType);
			}

			return dto.ContentHash is { } contentHash &&
				AssetContentHash.IsValid(contentHash) &&
				_assetCache.TryRead(contentHash, out var bytes, out var mimeType)
					? new MusicPlayerArtwork(bytes, mimeType)
					: null;
		}
		catch (RemoteCapabilityException)
		{
			return null;
		}
	}

	public Task PlayAsync(CancellationToken cancellationToken = default)
		=> InvokeCommandAsync(CapabilityOperations.MusicPlayer.Play,
			new MusicPlayerInstanceArguments { InstanceId = InstanceId },
			cancellationToken);

	protected Task PlayItemCoreAsync(MusicPlayerCatalogItem item, CancellationToken cancellationToken = default)
		=> InvokeCommandAsync(CapabilityOperations.MusicPlayer.PlayItem,
			new MusicPlayerPlayItemArguments { InstanceId = InstanceId, Item = MusicPlayerCatalogMapper.ToDto(item) },
			cancellationToken);

	public Task PauseAsync(CancellationToken cancellationToken = default)
		=> InvokeCommandAsync(CapabilityOperations.MusicPlayer.Pause,
			new MusicPlayerInstanceArguments { InstanceId = InstanceId },
			cancellationToken);

	public Task TogglePlayPauseAsync(CancellationToken cancellationToken = default)
		=> InvokeCommandAsync(CapabilityOperations.MusicPlayer.Toggle,
			new MusicPlayerInstanceArguments { InstanceId = InstanceId },
			cancellationToken);

	public Task NextAsync(CancellationToken cancellationToken = default)
		=> InvokeCommandAsync(CapabilityOperations.MusicPlayer.Next,
			new MusicPlayerInstanceArguments { InstanceId = InstanceId },
			cancellationToken);

	public Task PreviousAsync(CancellationToken cancellationToken = default)
		=> InvokeCommandAsync(CapabilityOperations.MusicPlayer.Previous,
			new MusicPlayerInstanceArguments { InstanceId = InstanceId },
			cancellationToken);

	public Task SeekAsync(TimeSpan position, CancellationToken cancellationToken = default)
		=> InvokeCommandAsync(CapabilityOperations.MusicPlayer.Seek,
			new MusicPlayerSeekArguments { InstanceId = InstanceId, PositionSeconds = position.TotalSeconds },
			cancellationToken);

	public Task SetVolumeAsync(int volumePercent, CancellationToken cancellationToken = default)
		=> InvokeCommandAsync(CapabilityOperations.MusicPlayer.Volume,
			new MusicPlayerVolumeArguments { InstanceId = InstanceId, VolumePercent = volumePercent },
			cancellationToken);

	public Task SetShuffleAsync(bool enabled, CancellationToken cancellationToken = default)
		=> InvokeCommandAsync(CapabilityOperations.MusicPlayer.Shuffle,
			new MusicPlayerShuffleArguments { InstanceId = InstanceId, Enabled = enabled },
			cancellationToken);

	public Task SetRepeatModeAsync(RepeatMode mode, CancellationToken cancellationToken = default)
		=> InvokeCommandAsync(CapabilityOperations.MusicPlayer.Repeat,
			new MusicPlayerRepeatArguments { InstanceId = InstanceId, Mode = mode.ToString() },
			cancellationToken);

	protected async Task<IReadOnlyList<MusicPlayerCatalogItem>> GetCatalogCoreAsync(
		MusicPlayerCatalogItemKind kind,
		string? filter,
		CancellationToken cancellationToken)
	{
		var data = await Invoker.InvokeAsync(PluginId,
				new CapabilityInvokeRequest
				{
					Kind = CapabilityKinds.MusicPlayer,
					LocalId = ProviderCapabilityId.LocalId,
					Operation = CapabilityOperations.MusicPlayer.Catalog,
					Arguments = new MusicPlayerCatalogArguments
						{ InstanceId = InstanceId, Kind = kind.ToString(), Filter = filter }
				},
				cancellationToken)
			.ConfigureAwait(false);

		var dto = data?.Deserialize<MusicPlayerCatalogResult>(PluginProtocolJson.Options);
		return dto is null ? [] : [.. dto.Items.Select(MusicPlayerCatalogMapper.ToDomain)];
	}

	protected async Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesCoreAsync(CancellationToken cancellationToken)
	{
		var data = await Invoker.InvokeAsync(PluginId,
				new CapabilityInvokeRequest
				{
					Kind = CapabilityKinds.MusicPlayer,
					LocalId = ProviderCapabilityId.LocalId,
					Operation = CapabilityOperations.MusicPlayer.Devices,
					Arguments = new MusicPlayerInstanceArguments { InstanceId = InstanceId }
				},
				cancellationToken)
			.ConfigureAwait(false);

		var dto = data?.Deserialize<MusicPlayerDevicesResult>(PluginProtocolJson.Options);
		return dto is null ? [] : [.. dto.Devices.Select(ToDomain)];
	}

	protected Task TransferPlaybackCoreAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken)
		=> InvokeCommandAsync(CapabilityOperations.MusicPlayer.Transfer,
			new MusicPlayerTransferArguments
				{ InstanceId = InstanceId, DeviceId = deviceId, StartPlayback = startPlayback },
			cancellationToken);

	private async Task InvokeCommandAsync(string operation, object arguments, CancellationToken cancellationToken)
		=> await Invoker.InvokeAsync(PluginId,
				new CapabilityInvokeRequest
				{
					Kind = CapabilityKinds.MusicPlayer, LocalId = ProviderCapabilityId.LocalId, Operation = operation,
					Arguments = arguments
				},
				cancellationToken)
			.ConfigureAwait(false);

	private static MusicPlayerDevice ToDomain(MusicPlayerDeviceDto dto)
		=> new(dto.Id, dto.Name, dto.Type, dto.IsActive, dto.VolumePercent);
}
