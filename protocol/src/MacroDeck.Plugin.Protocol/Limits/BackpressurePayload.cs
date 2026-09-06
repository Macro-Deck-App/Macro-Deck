namespace MacroDeck.Plugin.Protocol.Limits;

/// <summary>Payload of <c>flow.pause</c> and <c>flow.resume</c>.</summary>
public sealed record BackpressurePayload
{
	public required string Reason { get; init; }

	public int? ResumeAfterMs { get; init; }
}
