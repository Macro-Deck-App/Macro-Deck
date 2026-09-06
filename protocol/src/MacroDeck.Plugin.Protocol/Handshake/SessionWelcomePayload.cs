namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>Payload of <c>session.welcome</c>, the host's reply to <c>session.hello</c>.</summary>
public sealed record SessionWelcomePayload
{
	public required string SessionId { get; init; }

	/// <summary><c>true</c> when this connection resumed a prior session rather than starting one.</summary>
	public required bool Resumed { get; init; }
}
