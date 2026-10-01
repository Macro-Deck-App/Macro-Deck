using MacroDeck.Localization;

namespace MacroDeck.Sdk.VideoStreams;

/// <summary>
/// One source of video streams, registered through
/// <see cref="IVideoStreamProviderContext.RegisterProviderAsync" />. Macro Deck lists its streams and opens
/// a session per consumer that shows one; the provider hands back a description of where its media is. Macro
/// Deck fetches the media from there and relays it to the consumer, so the source only has to be reachable
/// from the computer Macro Deck runs on: it can bind to loopback and needs no credentials for consumers.
/// </summary>
/// <remarks>
/// <para>
/// <b>Every <see cref="OpenAsync" /> that returned a description gets exactly one
/// <see cref="CloseAsync" /></b> for that session id, unless the provider ended the session itself through
/// <see cref="IVideoStreamProviderContext.CloseSessionAsync" />. That holds even when the consumer went away
/// while the open was still running: the close then follows the open's return. A provider can release the
/// session's resources in <see cref="CloseAsync" /> and nowhere else.
/// </para>
/// <para>
/// Calls for different sessions can run concurrently. Macro Deck bounds every call by a timeout and treats
/// a call that times out as failed.
/// </para>
/// </remarks>
public interface IVideoStreamProvider
{
	/// <summary>
	/// Stable id, unique among every provider the plugin registers across all of its integrations. A
	/// resource local id: non-empty, at most 256 characters, no whitespace and no <c>::</c>. Consumers
	/// store it, so it must stay the same across restarts.
	/// </summary>
	string Id { get; }

	LocalizedText Name { get; }

	LocalizedText? Description => null;

	/// <summary>
	/// The streams this provider offers right now. At most 256; Macro Deck keeps the first 256 of a longer
	/// list. Call <see cref="IVideoStreamProviderContext.NotifyStreamsChangedAsync" /> when the list, a
	/// stream's metadata or its <see cref="VideoStreamState" /> changes.
	/// </summary>
	Task<IReadOnlyList<VideoStreamDescriptor>> GetStreamsAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Opens a session on a stream for one consumer and describes where Macro Deck fetches its media. The
	/// description's transport must be one of <see cref="VideoStreamOpenRequest.AcceptedTransports" />.
	/// </summary>
	/// <exception cref="VideoStreamException">The session cannot be opened. The code tells the consumer
	/// why, for example <see cref="VideoStreamErrorCode.TransportNotAccepted" />.</exception>
	Task<VideoStreamSessionDescription> OpenAsync(VideoStreamOpenRequest request, CancellationToken cancellationToken);

	/// <summary>
	/// The consumer stopped showing the stream for now, for example because the page is hidden. The
	/// default does nothing and keeps the session running.
	/// </summary>
	Task SuspendAsync(string sessionId, CancellationToken cancellationToken) => Task.CompletedTask;

	/// <summary>
	/// The consumer shows a suspended stream again. Returns a new description, or null when the previous
	/// one is still valid, which is the default.
	/// </summary>
	Task<VideoStreamSessionDescription?> ResumeAsync(string sessionId, CancellationToken cancellationToken)
		=> Task.FromResult<VideoStreamSessionDescription?>(null);

	/// <summary>
	/// Ends a session and releases whatever it holds. Called exactly once per opened session; see the
	/// remarks on this interface.
	/// </summary>
	Task CloseAsync(string sessionId, VideoStreamSessionReason reason, CancellationToken cancellationToken);
}
