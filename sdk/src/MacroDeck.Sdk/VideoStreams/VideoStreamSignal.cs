namespace MacroDeck.Sdk.VideoStreams;

/// <summary>
/// An opaque message between a provider and a consumer during a session, for example a WebRTC answer or
/// ICE candidate. Macro Deck relays it unchanged.
/// </summary>
/// <param name="Type">What the payload is, agreed between provider and consumer. At most 64 characters.</param>
/// <param name="Payload">At most 32768 characters.</param>
public sealed record VideoStreamSignal(string Type, string Payload);
