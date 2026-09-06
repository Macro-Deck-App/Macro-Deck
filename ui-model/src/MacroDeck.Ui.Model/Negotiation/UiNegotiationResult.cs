namespace MacroDeck.Ui.Model.Negotiation;

/// <summary>
/// The outcome of negotiating one subject - the UI model version itself, or one node's component.
/// A computation result, not a wire type: it never crosses the wire on its own, so it carries no
/// canonical serialization contract. Rejection is never fatal - it degrades to the dual-path field
/// list, or to a node's <c>Fallback</c>, and never fails the session.
/// </summary>
public sealed record UiNegotiationResult
{
	/// <summary>What was negotiated: <see cref="UiCapabilityNegotiator.ModelVersionSubject" /> for model
	/// version negotiation, or the node's <c>Type</c> for component negotiation.</summary>
	public required string Subject { get; init; }

	/// <summary>Whether the renderer supports <see cref="Subject" /> at the version required.</summary>
	public required bool IsSupported { get; init; }

	/// <summary>The negotiated version, set exactly when <see cref="IsSupported" /> is <c>true</c>.</summary>
	public int? NegotiatedVersion { get; init; }

	/// <summary>Why negotiation failed, set exactly when <see cref="IsSupported" /> is <c>false</c>.</summary>
	public string? FallbackReason { get; init; }

	/// <summary>Builds a supported result.</summary>
	public static UiNegotiationResult Accept(string subject, int negotiatedVersion)
		=> new() { Subject = subject, IsSupported = true, NegotiatedVersion = negotiatedVersion };

	/// <summary>Builds an unsupported result that degrades rather than throwing.</summary>
	public static UiNegotiationResult Fallback(string subject, string reason)
		=> new() { Subject = subject, IsSupported = false, FallbackReason = reason };
}
