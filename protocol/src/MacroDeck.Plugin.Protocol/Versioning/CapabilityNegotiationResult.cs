namespace MacroDeck.Plugin.Protocol.Versioning;

/// <summary>The outcome of negotiating one declared capability. Rejection is never fatal to the
/// session - a rejected capability just means the session proceeds degraded, without it.</summary>
public sealed record CapabilityNegotiationResult
{
	public required string Kind { get; init; }

	public required bool Accepted { get; init; }

	public int? NegotiatedVersion { get; init; }

	public string? RejectionReason { get; init; }

	public static CapabilityNegotiationResult Accept(string kind, int negotiatedVersion)
		=> new() { Kind = kind, Accepted = true, NegotiatedVersion = negotiatedVersion };

	public static CapabilityNegotiationResult Reject(string kind, string reason)
		=> new() { Kind = kind, Accepted = false, RejectionReason = reason };
}
