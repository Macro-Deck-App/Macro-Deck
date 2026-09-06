using MacroDeck.Plugin.Protocol.Envelope;

namespace MacroDeck.Plugin.Protocol.Correlation;

/// <summary>Pure resolution of the correlation rules, so they are code and not prose.</summary>
public static class CorrelationRules
{
	/// <summary>
	/// A reply type without <c>correlationId</c> is malformed. An unrecognised correlation is dropped
	/// and logged, never fatal - see <see cref="CorrelationOutcome.CorrelationUnknown" />. A late
	/// message for a correlation that already timed out is dropped silently, not reported as
	/// <see cref="CorrelationOutcome.CorrelationUnknown" />.
	/// </summary>
	public static CorrelationOutcome Resolve(
		string messageType,
		bool hasCorrelationId,
		bool isKnownCorrelation,
		bool wasTimedOut)
	{
		if (!hasCorrelationId)
		{
			return MessageTypes.RequiresCorrelation(messageType)
				? CorrelationOutcome.MalformedEnvelope
				: CorrelationOutcome.Accept;
		}

		if (wasTimedOut)
		{
			return CorrelationOutcome.Drop;
		}

		return isKnownCorrelation ? CorrelationOutcome.Accept : CorrelationOutcome.CorrelationUnknown;
	}
}
