namespace MacroDeck.Sdk.MusicPlayer;

/// <summary>
/// Optional capability of a music player integration that can list and switch playback devices
/// (e.g. Spotify Connect). The host uses it to populate the Play on Device / Transfer Playback
/// action parameter (config-time, via dynamic options) and the runtime pick dialog. Implement
/// alongside <see cref="IMusicPlayerProvider"/>; discovered by the host through interface casting
/// like the other provider capabilities. No <c>instanceId</c> parameter: the capability object is
/// already instance-scoped via <see cref="IMusicPlayerProvider.GetPlayer"/>.
/// </summary>
public interface IMusicPlayerDeviceProvider
{
	/// <summary>
	/// Returns the devices this provider can switch playback to.
	/// </summary>
	/// <remarks>
	/// <b>Throw when the read fails; return an empty list only when there are genuinely no devices.</b>
	/// The host cannot tell those apart on its own, and it has to: an empty list renders as "no
	/// devices", while a failure renders as "could not load, retry". Swallowing the error and
	/// returning empty reports a device list we never saw. Let <see cref="OperationCanceledException"/>
	/// propagate too - a cancelled read is the caller leaving, not a provider fault, and the host logs
	/// the two differently.
	/// </remarks>
	Task<IReadOnlyList<MusicPlayerDevice>> GetDevicesAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Transfers playback to the given device, optionally starting it.
	/// </summary>
	/// <remarks>
	/// This is a command, not a read: <b>log and return on failure instead of throwing</b>, so an
	/// action flow continues rather than aborting on a transfer the user cannot retry from where they
	/// are.
	/// </remarks>
	Task TransferPlaybackAsync(string deviceId, bool startPlayback, CancellationToken cancellationToken);
}
