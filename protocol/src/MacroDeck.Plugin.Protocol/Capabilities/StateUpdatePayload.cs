namespace MacroDeck.Plugin.Protocol.Capabilities;

/// <summary>
/// Payload of <c>state.update</c>. Means "this kind's snapshot is stale, re-describe it" -
/// deliberately not data-carrying. A per-kind diff protocol would be ten more contracts to keep
/// honest, for the sake of saving one round trip on a change the user just caused; a bare
/// invalidation signal followed by a fresh <c>describe</c> is cheap enough that the diff protocol
/// never earns its keep.
/// </summary>
public sealed record StateUpdatePayload
{
	/// <summary>One of <see cref="MacroDeck.Plugin.Protocol.Handshake.CapabilityKinds" />.</summary>
	public required string Kind { get; init; }

	/// <summary>The declared local id whose snapshot is stale. Absent when the whole kind is affected.</summary>
	/// <remarks>
	/// For <c>email-inbox</c> it names an inbox id instead, the one a <c>snapshot</c> argument carries: that
	/// inbox's messages, counts or status changed, and the host reads its snapshot again without
	/// re-describing the kind. Absent, the inbox list changed and the host reads it with <c>inboxes</c>.
	/// </remarks>
	public string? LocalId { get; init; }

	/// <summary>Why the snapshot went stale. Diagnostic only - the receiver never branches on it.</summary>
	public string? Reason { get; init; }
}
