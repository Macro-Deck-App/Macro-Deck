namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>Payload of <c>session.goodbye</c>: voluntary teardown, making the session non-resumable
/// at once.</summary>
public sealed record SessionGoodbyePayload
{
	public string? Reason { get; init; }
}
