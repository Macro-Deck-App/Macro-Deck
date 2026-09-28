using MacroDeck.Localization;

namespace MacroDeck.Sdk.VideoStreams;

/// <summary>
/// The host surface video stream providers register against. Handed to
/// <see cref="IVideoStreamIntegration.InitializeAsync" /> and safe to retain for as long as the integration
/// runs.
/// </summary>
/// <remarks>
/// Against a Macro Deck that predates video streams nothing throws: registering returns an empty
/// registration and every other call does nothing, so one plugin build serves old and new hosts.
/// </remarks>
public interface IVideoStreamProviderContext
{
	/// <summary>Registers a provider. A plugin can register at most 16.</summary>
	/// <returns>The host's identity for the provider. Both ids are empty when the host predates video
	/// streams and nothing was registered.</returns>
	/// <exception cref="ArgumentException">The provider's id is not a valid resource local id, another
	/// provider of this plugin is already registered under it, or the plugin already has 16
	/// providers.</exception>
	/// <exception cref="VideoStreamException">With <see cref="VideoStreamErrorCode.Busy" /> when Macro Deck
	/// still rate limits the registration after a few short, spaced retries. Nothing is registered
	/// then.</exception>
	Task<VideoStreamProviderRegistration> RegisterProviderAsync(
		IVideoStreamProvider provider,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Withdraws a provider. Its open sessions are closed first, each with one
	/// <see cref="IVideoStreamProvider.CloseAsync" /> and reason
	/// <see cref="VideoStreamSessionReason.ProviderRemoved" />; a session whose open is still running is
	/// closed as soon as the open returns. Unknown ids are ignored.
	/// </summary>
	Task UnregisterProviderAsync(string providerId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Tells Macro Deck the provider's streams changed: one was added or removed, or a stream's metadata or
	/// <see cref="VideoStreamState" /> changed. Macro Deck then reads
	/// <see cref="IVideoStreamProvider.GetStreamsAsync" /> again.
	/// </summary>
	/// <exception cref="VideoStreamException">With <see cref="VideoStreamErrorCode.Busy" /> when Macro Deck
	/// still rate limits the notice after a few short, spaced retries.</exception>
	Task NotifyStreamsChangedAsync(string providerId, CancellationToken cancellationToken = default);

	/// <summary>
	/// Reports a session's state, for example <see cref="VideoStreamSessionState.Reconnecting" /> while the
	/// source recovers and <see cref="VideoStreamSessionState.Active" /> once it has, optionally with a new
	/// description the consumer switches to. Ignored for a session that is no longer open.
	/// </summary>
	/// <param name="message">Text the consumer may show, in the reader's own language.</param>
	/// <exception cref="ArgumentException">The description exceeds a documented bound.</exception>
	/// <exception cref="VideoStreamException">With <see cref="VideoStreamErrorCode.Busy" /> when Macro Deck
	/// still rate limits the session's messages after a few short, spaced retries.</exception>
	Task UpdateSessionAsync(
		string sessionId,
		VideoStreamSessionState state,
		VideoStreamSessionDescription? description = null,
		VideoStreamSessionReason reason = VideoStreamSessionReason.None,
		LocalizedText? message = null,
		CancellationToken cancellationToken = default);

	/// <summary>
	/// Sends a signal to the session's consumer, for example a trickled ICE candidate. Signals of one
	/// session arrive in the order they were sent.
	/// </summary>
	/// <exception cref="ArgumentException">The signal exceeds a documented bound.</exception>
	/// <exception cref="VideoStreamException">With <see cref="VideoStreamErrorCode.Busy" /> when Macro Deck
	/// still rate limits the session's messages after a few short, spaced retries.</exception>
	Task SendSignalAsync(string sessionId, VideoStreamSignal signal, CancellationToken cancellationToken = default);

	/// <summary>
	/// Ends a session from the provider's side. Macro Deck does not call
	/// <see cref="IVideoStreamProvider.CloseAsync" /> for a session the provider closed itself. Unknown or
	/// already closed sessions are ignored.
	/// </summary>
	/// <remarks>
	/// The close waits behind the session's earlier updates and signals. The session accepts no further
	/// updates or signals once this is called, even when the close then fails; calling it again retries
	/// the close.
	/// </remarks>
	/// <exception cref="VideoStreamException">With <see cref="VideoStreamErrorCode.Busy" /> when Macro Deck
	/// still rate limits the session's messages after a few short, spaced retries.</exception>
	Task CloseSessionAsync(
		string sessionId,
		VideoStreamSessionReason reason = VideoStreamSessionReason.ProviderClosed,
		LocalizedText? message = null,
		CancellationToken cancellationToken = default);
}
