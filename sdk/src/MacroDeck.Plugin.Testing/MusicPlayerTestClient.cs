using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Testing.Internal;

namespace MacroDeck.Plugin.Testing;

/// <summary>
/// The <c>music-player</c> capability - the largest operation vocabulary of any kind. Argument and
/// result shapes live in <c>MacroDeck.Plugin.Protocol.Capabilities.MusicPlayer</c>; pass whichever
/// request type an operation expects (most carry at least the instance id, since a provider may serve
/// several players) and read the reply back with <see cref="CapabilityInvocationOutcome.DataAs{T}" />.
///
/// <para>
/// Arguments stay <see cref="object" /> here rather than a typed parameter per operation, unlike this
/// package's other capability clients: with 17 operations spread across nine distinct argument shapes -
/// <c>MusicPlayerInstanceArguments</c>, <c>MusicPlayerArtworkArguments</c>,
/// <c>MusicPlayerPlayItemArguments</c>, <c>MusicPlayerSeekArguments</c>, <c>MusicPlayerVolumeArguments</c>,
/// <c>MusicPlayerShuffleArguments</c>, <c>MusicPlayerRepeatArguments</c>, <c>MusicPlayerCatalogArguments</c>
/// and <c>MusicPlayerTransferArguments</c> - a typed parameter per method would not buy the polymorphism
/// protection it does for a single-shape kind; it would just be nine near-identical method signatures.
/// </para>
/// </summary>
public sealed class MusicPlayerTestClient
{
	private readonly ICapabilityInvoker _invoker;

	internal MusicPlayerTestClient(ICapabilityInvoker invoker) => _invoker = invoker;

	/// <summary>What this provider declares about itself.</summary>
	public Task<CapabilityInvocationOutcome> DescribeAsync(CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Describe, null, options);

	/// <summary>The player instances currently available.</summary>
	public Task<CapabilityInvocationOutcome> GetInstancesAsync(CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Instances, null, options);

	/// <summary>An instance's current playback state.</summary>
	public Task<CapabilityInvocationOutcome> GetStateAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.State, arguments, options);

	/// <summary>The current track's artwork.</summary>
	public Task<CapabilityInvocationOutcome> GetArtworkAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Artwork, arguments, options);

	/// <summary>Resumes playback.</summary>
	public Task<CapabilityInvocationOutcome> PlayAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Play, arguments, options);

	/// <summary>Plays a specific catalog item.</summary>
	public Task<CapabilityInvocationOutcome> PlayItemAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.PlayItem, arguments, options);

	/// <summary>Pauses playback.</summary>
	public Task<CapabilityInvocationOutcome> PauseAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Pause, arguments, options);

	/// <summary>Toggles between playing and paused.</summary>
	public Task<CapabilityInvocationOutcome> ToggleAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Toggle, arguments, options);

	/// <summary>Skips to the next track.</summary>
	public Task<CapabilityInvocationOutcome> NextAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Next, arguments, options);

	/// <summary>Skips to the previous track.</summary>
	public Task<CapabilityInvocationOutcome> PreviousAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Previous, arguments, options);

	/// <summary>Seeks within the current track.</summary>
	public Task<CapabilityInvocationOutcome> SeekAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Seek, arguments, options);

	/// <summary>Sets playback volume.</summary>
	public Task<CapabilityInvocationOutcome> SetVolumeAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Volume, arguments, options);

	/// <summary>Sets shuffle mode.</summary>
	public Task<CapabilityInvocationOutcome> SetShuffleAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Shuffle, arguments, options);

	/// <summary>Sets repeat mode.</summary>
	public Task<CapabilityInvocationOutcome> SetRepeatAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Repeat, arguments, options);

	/// <summary>Searches or lists the provider's catalog, for <c>IMusicPlayerCatalogProvider</c>.</summary>
	public Task<CapabilityInvocationOutcome> GetCatalogAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Catalog, arguments, options);

	/// <summary>Lists playback devices, for <c>IMusicPlayerDeviceProvider</c>.</summary>
	public Task<CapabilityInvocationOutcome> GetDevicesAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Devices, arguments, options);

	/// <summary>Transfers playback to another device, for <c>IMusicPlayerDeviceProvider</c>.</summary>
	public Task<CapabilityInvocationOutcome> TransferAsync(object arguments, CapabilityInvokeOptions? options = null)
		=> Invoke(CapabilityOperations.MusicPlayer.Transfer, arguments, options);

	private Task<CapabilityInvocationOutcome> Invoke(string operation,
		object? arguments,
		CapabilityInvokeOptions? options)
		=> _invoker.InvokeAsync(CapabilityKinds.MusicPlayer,
			ProviderCapabilityId.LocalId,
			operation,
			arguments,
			options);
}
