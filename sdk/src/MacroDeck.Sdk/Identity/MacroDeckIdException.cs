namespace MacroDeck.Sdk.Identity;

/// <summary>
/// Thrown when a capability id is rejected. Carries the four things a diagnostic needs to be
/// actionable - who owns the capability, what kind it is, which local id was refused, and why - so a
/// log line names the offending declaration instead of just reporting that something was invalid.
/// </summary>
public sealed class MacroDeckIdException : Exception
{
	public MacroDeckIdException(string ownerId, string capabilityType, string localId, string reason)
		: base($"{capabilityType} id '{localId}' declared by '{ownerId}' is invalid: {reason}")
	{
		OwnerId = ownerId;
		CapabilityType = capabilityType;
		LocalId = localId;
		Reason = reason;
	}

	public string OwnerId { get; }

	public string CapabilityType { get; }

	public string LocalId { get; }

	public string Reason { get; }
}
