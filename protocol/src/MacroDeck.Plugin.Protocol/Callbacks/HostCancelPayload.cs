namespace MacroDeck.Plugin.Protocol.Callbacks;

/// <summary>
/// Payload of <c>host.cancel</c>, correlated to the <c>host.invoke</c> it withdraws. Mirrors
/// <c>CapabilityCancelPayload</c>: best-effort, so an unknown or already-answered correlation is a
/// no-op, never an error.
/// </summary>
public sealed record HostCancelPayload
{
	/// <summary>Why the invocation was withdrawn. Diagnostic only - the receiver never branches on it.</summary>
	public string? Reason { get; init; }
}
