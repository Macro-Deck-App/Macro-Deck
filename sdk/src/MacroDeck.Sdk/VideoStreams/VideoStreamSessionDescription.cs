namespace MacroDeck.Sdk.VideoStreams;

/// <summary>
/// How a consumer plays a session's stream. Macro Deck passes it to the consumer unchanged and never
/// interprets it.
/// </summary>
/// <param name="Transport">
/// A lowercase token naming how the stream is delivered, for example <c>webrtc</c>, <c>whep</c>,
/// <c>hls</c> or <c>mjpeg</c>: 1 to 32 characters of <c>a-z</c>, <c>0-9</c>, <c>.</c>, <c>+</c> and
/// <c>-</c>. The vocabulary is open; a consumer only accepts transports it can play.
/// </param>
/// <param name="Url">Where the consumer fetches the stream, when the transport uses one. At most 2048
/// characters. It must be reachable from the consumer, see <see cref="VideoStreamConsumer" />.</param>
/// <param name="Parameters">Transport-specific settings. At most 32 entries, keys up to 64 and values up to
/// 2048 characters.</param>
/// <param name="Payload">A transport-specific document, for example a WebRTC offer. At most 65536
/// characters.</param>
/// <param name="ExpiresAt">When <paramref name="Url" /> or <paramref name="Payload" /> stops working, for
/// example a signed URL. The provider sends a new description through
/// <see cref="IVideoStreamProviderContext.UpdateSessionAsync" /> before then.</param>
public sealed record VideoStreamSessionDescription(
	string Transport,
	string? Url = null,
	IReadOnlyDictionary<string, string>? Parameters = null,
	string? Payload = null,
	DateTimeOffset? ExpiresAt = null);
