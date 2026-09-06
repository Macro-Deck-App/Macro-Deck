namespace MacroDeck.Plugin.Protocol.Logging;

/// <summary>
/// Payload of <c>log.publish</c> - a batch of structured log events a plugin's own sink forwards to
/// the host. Fire-and-forget on the wire: there is no reply message.
/// </summary>
public sealed record LogPublishPayload
{
	public required IReadOnlyList<LogEventDto> Events { get; init; }

	/// <summary>How many events the plugin's own sink dropped since the previous batch.</summary>
	public int? Dropped { get; init; }
}
