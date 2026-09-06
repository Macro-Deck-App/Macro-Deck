namespace MacroDeck.Plugin.Protocol.Correlation;

/// <summary>The result of resolving an inbound message against its correlation id. See
/// <see cref="CorrelationRules.Resolve" />.</summary>
public enum CorrelationOutcome
{
	/// <summary>Deliver the message.</summary>
	Accept,

	/// <summary>Drop silently - most commonly a late reply for a correlation that already timed out.</summary>
	Drop,

	/// <summary>A reply type arrived without a <c>correlationId</c>.</summary>
	MalformedEnvelope,

	/// <summary>The <c>correlationId</c> does not match any in-flight message. Non-fatal, but reported.</summary>
	CorrelationUnknown,
}
