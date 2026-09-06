namespace MacroDeck.Plugin.Protocol.Correlation;

/// <summary>The result of resolving a <c>capability.cancel</c>. See
/// <see cref="CancellationRules.Resolve" />.</summary>
public enum CancellationOutcome
{
	/// <summary>Emit exactly one <c>capability.result</c> with a cancelled outcome.</summary>
	EmitCancelledResult,

	/// <summary>Do nothing - either the correlation is unknown, or a reply was already sent.</summary>
	NoOp,
}
