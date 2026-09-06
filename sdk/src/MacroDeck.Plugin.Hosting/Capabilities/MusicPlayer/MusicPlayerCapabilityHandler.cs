using System.Text.Json;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Sdk;
using MacroDeck.Sdk.MusicPlayer;

namespace MacroDeck.Plugin.Hosting.Capabilities.MusicPlayer;

/// <summary>
/// Exposes every registered integration's <c>IMusicPlayerProvider</c> as the <c>music-player</c>
/// capability. Provider-shaped like <c>events</c>/<c>issues</c>: the whole plugin declares one
/// <c>provider</c> local id, and every per-player operation carries its own <c>instanceId</c> in the
/// arguments, because music player instance ids are config-entry GUIDs that do not exist at
/// declaration time - see <see cref="ProviderCapabilityId" />'s remarks.
/// </summary>
internal sealed class MusicPlayerCapabilityHandler(
	IEnumerable<IPluginIntegration> integrations,
	PluginMetadata metadata,
	IPluginAssetUploader assetUploader) : ICapabilityHandler
{
	/// <summary>
	/// Artwork at or below this size is returned inline as base64 (base64 inflates it to roughly
	/// 170 KB, safely under <see cref="ProtocolLimits.MaxMessageBytes" />'s 256 KB). Above it, the
	/// artwork travels over the <c>asset.*</c> pipeline instead - uploaded before this operation
	/// replies, so the host's asset cache already has the bytes by the time it reads
	/// <see cref="MusicPlayerArtworkResult.ContentHash" /> back out of the result.
	/// </summary>
	private const int InlineArtworkThresholdBytes = 128 * 1024;

	private static readonly CapabilityVersionRange _version = new() { Minimum = 1, Maximum = 1 };

	private readonly IReadOnlyList<IMusicPlayerProvider> _providers = [.. integrations.OfType<IMusicPlayerProvider>()];

	public string Kind => CapabilityKinds.MusicPlayer;

	public IReadOnlyList<DeclaredCapability> DeclareCapabilities()
		=> _providers.Count == 0
			? []
			:
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.MusicPlayer, LocalId = ProviderCapabilityId.LocalId, VersionRange = _version
				}
			];

	public Task<CapabilityInvocationResult> InvokeAsync(
		CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(invocation);

		// describe ignores the local id entirely - see EventsCapabilityHandler's identical remark.
		if (string.Equals(invocation.Operation, CapabilityOperations.MusicPlayer.Describe, StringComparison.Ordinal))
		{
			return Task.FromResult(Describe());
		}

		if (!string.Equals(invocation.LocalId, ProviderCapabilityId.LocalId, StringComparison.Ordinal))
		{
			return Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"No music player provider '{invocation.LocalId}' is registered in this plugin."));
		}

		return invocation.Operation switch
		{
			CapabilityOperations.MusicPlayer.Instances => Task.FromResult(Instances()),
			CapabilityOperations.MusicPlayer.State => StateAsync(invocation, cancellationToken),
			CapabilityOperations.MusicPlayer.Artwork => ArtworkAsync(invocation, cancellationToken),
			CapabilityOperations.MusicPlayer.Play => RunAsync(invocation,
				(player, ct) => player.PlayAsync(ct),
				cancellationToken),
			CapabilityOperations.MusicPlayer.PlayItem => PlayItemAsync(invocation, cancellationToken),
			CapabilityOperations.MusicPlayer.Pause => RunAsync(invocation,
				(player, ct) => player.PauseAsync(ct),
				cancellationToken),
			CapabilityOperations.MusicPlayer.Toggle
				=> RunAsync(invocation, (player, ct) => player.TogglePlayPauseAsync(ct), cancellationToken),
			CapabilityOperations.MusicPlayer.Next => RunAsync(invocation,
				(player, ct) => player.NextAsync(ct),
				cancellationToken),
			CapabilityOperations.MusicPlayer.Previous
				=> RunAsync(invocation, (player, ct) => player.PreviousAsync(ct), cancellationToken),
			CapabilityOperations.MusicPlayer.Seek => SeekAsync(invocation, cancellationToken),
			CapabilityOperations.MusicPlayer.Volume => VolumeAsync(invocation, cancellationToken),
			CapabilityOperations.MusicPlayer.Shuffle => ShuffleAsync(invocation, cancellationToken),
			CapabilityOperations.MusicPlayer.Repeat => RepeatAsync(invocation, cancellationToken),
			CapabilityOperations.MusicPlayer.Catalog => CatalogAsync(invocation, cancellationToken),
			CapabilityOperations.MusicPlayer.Devices => DevicesAsync(invocation, cancellationToken),
			CapabilityOperations.MusicPlayer.Transfer => TransferAsync(invocation, cancellationToken),
			_ => Task.FromResult(CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnsupported,
				$"The music-player capability has no operation '{invocation.Operation}'."))
		};
	}

	private CapabilityInvocationResult Describe()
	{
		var payload = new MusicPlayerDescribePayload
		{
			ProviderName = _providers.Select(p => p.ProviderName).FirstOrDefault(name => !string.IsNullOrEmpty(name)) ??
				metadata.Name,
			Instances = BuildInstances()
		};

		return CapabilityInvocationResult.Ok(payload);
	}

	/// <summary>
	/// The <c>instances</c> operation: the same instance list <c>describe</c> carries, exposed as its
	/// own narrow round trip - see <see cref="MusicPlayerInstancesResult" />'s remarks.
	/// </summary>
	private CapabilityInvocationResult Instances() => CapabilityInvocationResult.Ok(new MusicPlayerInstancesResult
	{
		Instances = BuildInstances()
	});

	/// <summary>
	/// Never re-sorted, and always walks providers/instances in the same order: a music player
	/// instance's position in this list is what <c>MusicPlayerRegistry.GetPlayer</c> indexes into for
	/// its default-instance resolution on the host side.
	/// </summary>
	private IReadOnlyList<MusicPlayerInstanceDto> BuildInstances()
		=>
		[
			.. _providers.SelectMany(provider => provider.GetInstances()
				.Select(instance => MusicPlayerDescriptorMapper.ToDto(instance, provider.GetPlayer(instance.Id))))
		];

	private async Task<CapabilityInvocationResult> StateAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		if (!TryResolvePlayer(invocation, out var player, out var failure))
		{
			return failure!;
		}

		var state = await player.GetStateAsync(cancellationToken).ConfigureAwait(false);
		return CapabilityInvocationResult.Ok(MusicPlayerDescriptorMapper.ToDto(state));
	}

	private async Task<CapabilityInvocationResult> ArtworkAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<MusicPlayerArtworkArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The artwork operation requires arguments.");
		}

		if (!TryResolvePlayer(arguments.InstanceId, out var player, out var failure))
		{
			return failure!;
		}

		var artwork = await player.GetArtworkAsync(arguments.ArtworkId, cancellationToken).ConfigureAwait(false);
		if (artwork is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Artwork '{arguments.ArtworkId}' is not available.");
		}

		if (artwork.Data.Length <= InlineArtworkThresholdBytes)
		{
			return CapabilityInvocationResult.Ok(new MusicPlayerArtworkResult
			{
				Data = Convert.ToBase64String(artwork.Data), MimeType = artwork.MimeType
			});
		}

		if (artwork.Data.Length > ProtocolLimits.MaxAssetBytes)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.AssetTooLarge,
				$"Artwork '{arguments.ArtworkId}' is {artwork.Data.Length} bytes, over the {ProtocolLimits.MaxAssetBytes} byte asset limit.");
		}

		try
		{
			var contentHash = await assetUploader
				.UploadAsync(AssetKinds.Artwork, artwork.MimeType, artwork.Data, cancellationToken)
				.ConfigureAwait(false);

			return CapabilityInvocationResult.Ok(new MusicPlayerArtworkResult
				{ ContentHash = contentHash, MimeType = artwork.MimeType });
		}
		catch (AssetUploadException exception)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Uploading artwork '{arguments.ArtworkId}' failed: {exception.Message}");
		}
	}

	private async Task<CapabilityInvocationResult> PlayItemAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<MusicPlayerPlayItemArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The play-item operation requires arguments.");
		}

		if (!TryResolvePlayer(arguments.InstanceId, out var player, out var failure))
		{
			return failure!;
		}

		if (player is not ICatalogMusicPlayer catalogPlayer)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Music player '{arguments.InstanceId}' does not support playing catalog items.");
		}

		await catalogPlayer.PlayItemAsync(MusicPlayerDescriptorMapper.ToDomain(arguments.Item), cancellationToken)
			.ConfigureAwait(false);
		return CapabilityInvocationResult.Ok();
	}

	private async Task<CapabilityInvocationResult> SeekAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<MusicPlayerSeekArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The seek operation requires arguments.");
		}

		if (!TryResolvePlayer(arguments.InstanceId, out var player, out var failure))
		{
			return failure!;
		}

		await player.SeekAsync(TimeSpan.FromSeconds(arguments.PositionSeconds), cancellationToken)
			.ConfigureAwait(false);
		return CapabilityInvocationResult.Ok();
	}

	private async Task<CapabilityInvocationResult> VolumeAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<MusicPlayerVolumeArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The volume operation requires arguments.");
		}

		if (!TryResolvePlayer(arguments.InstanceId, out var player, out var failure))
		{
			return failure!;
		}

		await player.SetVolumeAsync(arguments.VolumePercent, cancellationToken).ConfigureAwait(false);
		return CapabilityInvocationResult.Ok();
	}

	private async Task<CapabilityInvocationResult> ShuffleAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<MusicPlayerShuffleArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The shuffle operation requires arguments.");
		}

		if (!TryResolvePlayer(arguments.InstanceId, out var player, out var failure))
		{
			return failure!;
		}

		await player.SetShuffleAsync(arguments.Enabled, cancellationToken).ConfigureAwait(false);
		return CapabilityInvocationResult.Ok();
	}

	private async Task<CapabilityInvocationResult> RepeatAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<MusicPlayerRepeatArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The repeat operation requires arguments.");
		}

		if (!TryResolvePlayer(arguments.InstanceId, out var player, out var failure))
		{
			return failure!;
		}

		if (!Enum.TryParse<RepeatMode>(arguments.Mode, ignoreCase: false, out var mode))
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				$"Unknown repeat mode '{arguments.Mode}'.");
		}

		await player.SetRepeatModeAsync(mode, cancellationToken).ConfigureAwait(false);
		return CapabilityInvocationResult.Ok();
	}

	/// <summary>
	/// <c>IMusicPlayerCatalogProvider.GetCatalogAsync</c> must throw on a read failure rather than
	/// degrade to an empty list (see its doc comments) - so unlike every other operation here, this one
	/// deliberately does not catch anything beyond deserialization/resolution failures. A thrown
	/// exception propagates out of <see cref="InvokeAsync" /> and becomes a redacted
	/// <see cref="ProtocolErrorCodes.InternalError" /> the same way any other capability handler
	/// exception does (see <c>CapabilityDispatcher.RunAsync</c>), which the host adapter then lets
	/// propagate rather than swallowing - see <c>RemoteMusicPlayer.GetCatalogCoreAsync</c>.
	/// </summary>
	private async Task<CapabilityInvocationResult> CatalogAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<MusicPlayerCatalogArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The catalog operation requires arguments.");
		}

		if (!TryResolvePlayer(arguments.InstanceId, out var player, out var failure))
		{
			return failure!;
		}

		if (player is not IMusicPlayerCatalogProvider catalogProvider)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Music player '{arguments.InstanceId}' does not support browsing its catalog.");
		}

		if (!Enum.TryParse<MusicPlayerCatalogItemKind>(arguments.Kind, ignoreCase: false, out var kind))
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				$"Unknown catalog item kind '{arguments.Kind}'.");
		}

		var items = await catalogProvider
			.GetCatalogAsync(arguments.InstanceId, kind, arguments.Filter, cancellationToken)
			.ConfigureAwait(false);

		return CapabilityInvocationResult.Ok(new MusicPlayerCatalogResult
		{
			Items = [.. items.Select(MusicPlayerDescriptorMapper.ToDto)]
		});
	}

	/// <summary><c>IMusicPlayerDeviceProvider.GetDevicesAsync</c> throws on failure too - see
	/// <see cref="CatalogAsync" />'s identical remarks.</summary>
	private async Task<CapabilityInvocationResult> DevicesAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<MusicPlayerInstanceArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The devices operation requires arguments.");
		}

		if (!TryResolvePlayer(arguments.InstanceId, out var player, out var failure))
		{
			return failure!;
		}

		if (player is not IMusicPlayerDeviceProvider deviceProvider)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Music player '{arguments.InstanceId}' does not support device switching.");
		}

		var devices = await deviceProvider.GetDevicesAsync(cancellationToken).ConfigureAwait(false);

		return CapabilityInvocationResult.Ok(new MusicPlayerDevicesResult
		{
			Devices = [.. devices.Select(MusicPlayerDescriptorMapper.ToDto)]
		});
	}

	private async Task<CapabilityInvocationResult> TransferAsync(CapabilityInvocation invocation,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<MusicPlayerTransferArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				"The transfer operation requires arguments.");
		}

		if (!TryResolvePlayer(arguments.InstanceId, out var player, out var failure))
		{
			return failure!;
		}

		if (player is not IMusicPlayerDeviceProvider deviceProvider)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
				$"Music player '{arguments.InstanceId}' does not support device switching.");
		}

		await deviceProvider.TransferPlaybackAsync(arguments.DeviceId, arguments.StartPlayback, cancellationToken)
			.ConfigureAwait(false);
		return CapabilityInvocationResult.Ok();
	}

	private async Task<CapabilityInvocationResult> RunAsync(
		CapabilityInvocation invocation,
		Func<IMusicPlayer, CancellationToken, Task> run,
		CancellationToken cancellationToken)
	{
		var arguments = invocation.Arguments?.Deserialize<MusicPlayerInstanceArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			return CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				$"The {invocation.Operation} operation requires arguments.");
		}

		if (!TryResolvePlayer(arguments.InstanceId, out var player, out var failure))
		{
			return failure!;
		}

		await run(player, cancellationToken).ConfigureAwait(false);
		return CapabilityInvocationResult.Ok();
	}

	private bool TryResolvePlayer(
		CapabilityInvocation invocation,
		out IMusicPlayer player,
		out CapabilityInvocationResult? failure)
	{
		var arguments = invocation.Arguments?.Deserialize<MusicPlayerInstanceArguments>(PluginProtocolJson.Options);
		if (arguments is null)
		{
			player = null!;
			failure = CapabilityInvocationResult.Failed(ProtocolErrorCodes.InvalidPayload,
				$"The {invocation.Operation} operation requires arguments.");
			return false;
		}

		return TryResolvePlayer(arguments.InstanceId, out player, out failure);
	}

	private bool TryResolvePlayer(string instanceId, out IMusicPlayer player, out CapabilityInvocationResult? failure)
	{
		foreach (var provider in _providers)
		{
			if (provider.GetInstances()
				.Any(instance => string.Equals(instance.Id, instanceId, StringComparison.Ordinal)))
			{
				var resolved = provider.GetPlayer(instanceId);
				if (resolved is not null)
				{
					player = resolved;
					failure = null;
					return true;
				}
			}
		}

		player = null!;
		failure = CapabilityInvocationResult.Failed(ProtocolErrorCodes.CapabilityUnavailable,
			$"No music player instance '{instanceId}' is currently available.");
		return false;
	}
}
