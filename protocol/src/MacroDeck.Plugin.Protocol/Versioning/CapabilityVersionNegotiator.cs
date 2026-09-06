using MacroDeck.Plugin.Protocol.Handshake;

namespace MacroDeck.Plugin.Protocol.Versioning;

/// <summary>Per-capability version negotiation. Runs the same algorithm as
/// <see cref="ProtocolVersionNegotiator" /> per kind, but a failure is never fatal to the session -
/// an unsupported or unknown kind is simply rejected and the session proceeds without it.</summary>
public static class CapabilityVersionNegotiator
{
	public static CapabilityNegotiationResult Negotiate(string kind, CapabilityVersionRange clientRange)
	{
		if (!CapabilityKinds.IsKnown(kind))
		{
			return CapabilityNegotiationResult.Reject(kind, "Unknown capability kind.");
		}

		var negotiated = Math.Min(clientRange.Maximum, ProtocolVersions.Current);
		var floor = Math.Max(clientRange.Minimum, ProtocolVersions.Minimum);

		return negotiated < floor
			? CapabilityNegotiationResult.Reject(kind, "No overlapping capability version.")
			: CapabilityNegotiationResult.Accept(kind, negotiated);
	}
}
