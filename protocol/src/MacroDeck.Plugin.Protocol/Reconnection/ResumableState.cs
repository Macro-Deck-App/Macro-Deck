using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Reconnection;

/// <summary>
/// What a resumed session carries forward: session id, negotiated version, negotiated capability map,
/// declared catalogue and the host-side idempotency cache. Deliberately excludes in-flight
/// invocations, event subscriptions and queued outbound messages - none of those survive a resume.
/// </summary>
public sealed record ResumableState
{
	public required string SessionId { get; init; }

	public required int NegotiatedVersion { get; init; }

	public required IReadOnlyDictionary<string, CapabilityNegotiationResult> NegotiatedCapabilities { get; init; }

	public required IReadOnlyList<DeclaredCapability> DeclaredCapabilities { get; init; }

	/// <summary>Keyed by idempotency key. Host-side only and survives a resume; a restarted plugin
	/// process re-executes, which is the honest consequence of at-most-once delivery.</summary>
	public required IReadOnlyDictionary<string, string> IdempotencyCache { get; init; }
}
