using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Testing.Internal;

/// <summary>
/// Bookkeeping for one session <c>MacroDeckTestHost</c> has issued, from <c>POST /sessions</c> until it
/// is forgotten. Outlives any single socket - that is what makes a resume possible - so it is looked up
/// by session token on a WebSocket upgrade and again on every <c>session.hello</c>.
/// </summary>
internal sealed class SessionRecord
{
	public required string SessionId { get; init; }

	public required string SessionToken { get; init; }

	public required string PluginId { get; init; }

	public required int NegotiatedVersion { get; init; }

	public required IReadOnlyList<DeclaredCapability> Declared { get; set; }

	public required IReadOnlyList<CapabilityNegotiationResult> Accepted { get; set; }

	/// <summary>Set when the socket serving this session drops, cleared again on a genuine resume.
	/// Null while a connection is live.</summary>
	public DateTimeOffset? DroppedAt { get; set; }

	/// <summary>Set once the session has been ended for good (goodbye, delete, or replaced) - a dropped
	/// session with this set is never resumed even if a resume attempt arrives inside the window.</summary>
	public bool Ended { get; set; }
}
