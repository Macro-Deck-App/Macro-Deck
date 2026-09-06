using MacroDeck.Plugin.Protocol.Errors;

namespace MacroDeck.Plugin.Protocol.Envelope;

/// <summary>
/// The result of <see cref="ProtocolEnvelopeReader.Read" />. A record rather than two <c>out</c>
/// parameters - a two-out shape cannot be annotated to compile clean under <c>Nullable=enable</c>, and
/// the compiler cannot check exhaustiveness on it. <see cref="Envelope" /> can be set even when
/// <see cref="Succeeded" /> is <c>false</c>: an unknown message type still parses to a well-formed
/// envelope, and the caller needs its <c>id</c> to reply with <c>correlationId</c> set.
/// </summary>
public sealed record ProtocolReadResult
{
	public required bool Succeeded { get; init; }

	public ProtocolEnvelope? Envelope { get; init; }

	public ProtocolError? Error { get; init; }

	public static ProtocolReadResult Success(ProtocolEnvelope envelope)
		=> new() { Succeeded = true, Envelope = envelope };

	public static ProtocolReadResult Failure(ProtocolError error, ProtocolEnvelope? envelope = null)
		=> new() { Succeeded = false, Error = error, Envelope = envelope };
}
