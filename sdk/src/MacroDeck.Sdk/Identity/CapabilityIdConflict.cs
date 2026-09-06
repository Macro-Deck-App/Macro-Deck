namespace MacroDeck.Sdk.Identity;

/// <summary>
/// One reason a capability id was refused. Carries the four things that make the diagnostic
/// actionable - who declared it, which capability it belongs to, the offending local id, and why - so
/// an author can find the declaration without reading the host's source.
/// </summary>
/// <param name="OwnerId">The declaring owner's id, or the empty string when that is what is wrong.</param>
/// <param name="CapabilityType">What kind of thing was declared, e.g. "Integration", "Action", "Event".</param>
/// <param name="LocalId">The id as declared.</param>
public sealed record CapabilityIdConflict(string OwnerId, string CapabilityType, string LocalId, string Reason)
{
	public override string ToString()
		=> string.IsNullOrEmpty(OwnerId)
			? $"{CapabilityType} '{LocalId}': {Reason}"
			: $"{CapabilityType} '{LocalId}' declared by '{OwnerId}': {Reason}";
}
