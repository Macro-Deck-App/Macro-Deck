namespace MacroDeck.Sdk.VideoStreams;

/// <summary>
/// The consumer a session is opened for. A provider uses it to hand out a URL the consumer can reach: a
/// <c>localhost</c> URL only works for <see cref="VideoStreamConnectionKind.Local" />, a LAN URL needs
/// <see cref="VideoStreamConnectionKind.Network" />.
/// </summary>
/// <param name="DeviceId">Macro Deck's id of the device showing the stream, when the consumer is one.</param>
/// <param name="HostAddress">The address the consumer used to reach Macro Deck, when known. A provider
/// running next to Macro Deck can serve the stream from the same host name.</param>
/// <param name="ConnectionKind">How the consumer is connected.</param>
public sealed record VideoStreamConsumer(
	string? DeviceId,
	Uri? HostAddress,
	VideoStreamConnectionKind ConnectionKind);
