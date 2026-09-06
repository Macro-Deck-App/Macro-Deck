namespace MacroDeck.Sdk.Identity;

/// <summary>
/// The rule every declared capability id is checked against: each id is a legal
/// <see cref="LocalIdKind.Declared" /> id, and no id repeats within its owner.
///
/// <para>
/// It lives in the SDK rather than in the host because three populations need the identical answer -
/// the host validating an in-process integration, a plugin validating its own catalogue before it
/// declares anything, and the host validating what a plugin declared over the wire. A rule that
/// exists in three copies is a rule that eventually disagrees with itself.
/// </para>
/// </summary>
public static class DeclaredIdValidator
{
	/// <summary>
	/// Appends a conflict to <paramref name="conflicts" /> for every illegal or repeated id in
	/// <paramref name="localIds" />. Uniqueness is scoped to this call, so a caller checking several
	/// capability types for one owner gets per-type uniqueness by calling once per type - which is
	/// what "unique within an integration" has always meant.
	/// </summary>
	/// <param name="conflicts">Collected conflicts, appended to.</param>
	/// <param name="ownerId">The declaring owner, already validated.</param>
	/// <param name="capabilityType">Names the declaration in the diagnostic, e.g. "Action".</param>
	/// <param name="localIds">The ids as declared.</param>
	public static void Validate(
		ICollection<CapabilityIdConflict> conflicts,
		string ownerId,
		string capabilityType,
		IEnumerable<string> localIds)
	{
		ArgumentNullException.ThrowIfNull(conflicts);
		ArgumentNullException.ThrowIfNull(localIds);

		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var localId in localIds)
		{
			if (!MacroDeckId.TryValidateLocalId(localId, LocalIdKind.Declared, out var error))
			{
				conflicts.Add(new CapabilityIdConflict(ownerId, capabilityType, localId ?? string.Empty, error!));
				continue;
			}

			if (!seen.Add(localId))
			{
				conflicts.Add(new CapabilityIdConflict(ownerId,
					capabilityType,
					localId,
					$"Declared more than once. {capabilityType} ids must be unique within an owner."));
			}
		}
	}

	/// <summary>
	/// Validates an owner id, returning the conflict that refuses it or <c>null</c> when it is legal.
	/// A bad owner id is terminal: nothing it declares can be qualified, so a caller stops here.
	/// </summary>
	public static CapabilityIdConflict? ValidateOwner(
		string? ownerId,
		OwnerIdKind kind = OwnerIdKind.Package,
		string capabilityType = "Integration")
	{
		if (MacroDeckId.TryValidateOwnerId(ownerId, kind, out var error))
		{
			return null;
		}

		return new CapabilityIdConflict(string.Empty, capabilityType, ownerId ?? string.Empty, error!);
	}
}
