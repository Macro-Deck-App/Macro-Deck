namespace MacroDeck.Sdk.VideoStreams;

/// <summary>A consumer's request to open a session on a stream.</summary>
/// <param name="SessionId">Minted by Macro Deck. Every later call for the session names it.</param>
/// <param name="StreamId">One of the ids <see cref="IVideoStreamProvider.GetStreamsAsync" /> listed.</param>
/// <param name="AcceptedTransports">The transports the consumer can play, most preferred first. Macro Deck
/// offers <c>hls</c> and <c>mjpeg</c> at most.</param>
public sealed record VideoStreamOpenRequest(
	string SessionId,
	string StreamId,
	IReadOnlyList<string> AcceptedTransports);
