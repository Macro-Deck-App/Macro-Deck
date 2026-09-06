namespace MacroDeck.Plugin.Protocol.Versioning;

/// <summary>Pure per-session version negotiation. Runs once, at <c>POST /sessions</c> - <c>session.hello</c>
/// only asserts the already-negotiated version, it never re-negotiates.</summary>
public static class ProtocolVersionNegotiator
{
	/// <summary><c>negotiated = min(clientMax, Current)</c>, failing when
	/// <c>negotiated &lt; max(clientMin, Minimum)</c>.</summary>
	public static ProtocolVersionNegotiationOutcome Negotiate(ProtocolVersionRange clientRange)
	{
		var hostRange = new ProtocolVersionRange
		{
			Minimum = ProtocolVersions.Minimum,
			Maximum = ProtocolVersions.Current,
		};

		var negotiated = Math.Min(clientRange.Maximum, ProtocolVersions.Current);
		var floor = Math.Max(clientRange.Minimum, ProtocolVersions.Minimum);

		return negotiated < floor
			? ProtocolVersionNegotiationOutcome.Failure(hostRange)
			: ProtocolVersionNegotiationOutcome.Success(negotiated, hostRange);
	}
}
