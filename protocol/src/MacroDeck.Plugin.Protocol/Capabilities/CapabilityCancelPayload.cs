namespace MacroDeck.Plugin.Protocol.Capabilities;

/// <summary>
/// Payload of <c>capability.cancel</c>, correlated to the <c>capability.invoke</c> it withdraws.
/// Best-effort: an unknown or already-answered correlation is a no-op, never an error. See
/// <c>CancellationRules</c>.
/// </summary>
public sealed record CapabilityCancelPayload
{
	/// <summary>Why the invocation was withdrawn. Diagnostic only - the receiver never branches on it.</summary>
	public string? Reason { get; init; }
}
