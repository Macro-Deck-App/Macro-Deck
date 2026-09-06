using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Capabilities;

/// <summary>
/// Payload of <c>capability.declare.ack</c>, correlated to the <c>capability.declare</c> it answers.
/// Carries one result per declared capability, in the same shape the session response uses, so a
/// mid-session declaration and a handshake declaration are read the same way.
/// </summary>
public sealed record CapabilityDeclareAckPayload
{
	public required IReadOnlyList<CapabilityNegotiationResult> Capabilities { get; init; }
}
