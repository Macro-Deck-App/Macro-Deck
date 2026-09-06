namespace MacroDeck.Plugin.Protocol.Correlation;

/// <summary>Pure resolution of the cancellation rules. Cancel is best-effort: cancelling an unknown
/// correlation is a no-op, not an error, because cancel and result legitimately cross on the wire.</summary>
public static class CancellationRules
{
	public static CancellationOutcome Resolve(bool isKnownCorrelation, bool alreadyReplied)
	{
		if (!isKnownCorrelation)
		{
			return CancellationOutcome.NoOp;
		}

		return alreadyReplied ? CancellationOutcome.NoOp : CancellationOutcome.EmitCancelledResult;
	}
}
