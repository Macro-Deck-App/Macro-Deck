namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>
/// Payload of <c>session.hello</c>. Asserts the already-negotiated version and session id - a mismatch
/// is <c>PROTOCOL_VERSION_UNSUPPORTED</c> and closes the socket; this message never re-negotiates.
/// </summary>
public sealed record SessionHelloPayload
{
	public required int ProtocolVersion { get; init; }

	public required string SessionId { get; init; }

	/// <summary>Present when this connection is attempting to resume a session that survived a prior
	/// drop, rather than replacing it.</summary>
	public string? ResumeSessionId { get; init; }

	/// <summary>Identifies the connecting process instance, so a fresh connection without
	/// <see cref="ResumeSessionId" /> can be told apart from a genuine resume under
	/// <c>MaxSessionsPerPlugin = 1</c>.</summary>
	public string? InstanceId { get; init; }
}
