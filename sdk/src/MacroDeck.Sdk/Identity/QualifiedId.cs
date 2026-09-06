namespace MacroDeck.Sdk.Identity;

/// <summary>
/// A capability id namespaced by the integration or plugin that owns it, serialized as
/// <c>ownerId::localId</c>. Integration and plugin authors declare only the local half; the host
/// derives the qualified form, which is what keeps ids globally unique while each owner stays unaware
/// of the others (see ADR 0004).
///
/// <para>
/// Every instance has been validated: the constructor is private and the factories reject a malformed
/// owner id, a malformed local id, and a local id that is itself qualified - so a caller cannot claim
/// another owner's namespace by smuggling a <c>::</c> into the local half.
/// </para>
///
/// <para>
/// Use <see cref="Create" /> only for compile-time declarations that registration has already
/// validated. Anything read back out of persisted data, or handed in by integration code at runtime,
/// goes through <see cref="TryCreate(string?,string?,out QualifiedId)" /> or
/// <see cref="TryParse" />: a stored profile predates these rules and must be skipped, never thrown
/// out of a load path.
/// </para>
/// </summary>
public readonly record struct QualifiedId
{
	public const string Separator = "::";

	private readonly string? _ownerId;
	private readonly string? _localId;

	private QualifiedId(string ownerId, string localId)
	{
		_ownerId = ownerId;
		_localId = localId;
	}

	public string OwnerId => _ownerId ?? string.Empty;

	public string LocalId => _localId ?? string.Empty;

	/// <summary>True for <c>default(QualifiedId)</c>, which names nothing.</summary>
	public bool IsEmpty => string.IsNullOrEmpty(_ownerId);

	/// <summary>Canonical serialization. The separator is exactly two characters and stays that way -
	/// the trigger editor slices a local id off by adding its length to the owner id's.</summary>
	public override string ToString() => IsEmpty ? string.Empty : _ownerId + Separator + _localId;

	/// <summary>
	/// Builds a qualified id from a declared owner and local id, throwing when either is invalid.
	/// </summary>
	/// <exception cref="MacroDeckIdException">The owner or local id does not satisfy its rule.</exception>
	public static QualifiedId Create(
		string ownerId,
		string localId,
		OwnerIdKind ownerKind = OwnerIdKind.Package,
		LocalIdKind localKind = LocalIdKind.Declared,
		string capabilityType = "Capability")
	{
		if (!MacroDeckId.TryValidateOwnerId(ownerId, ownerKind, out var ownerError))
		{
			throw new MacroDeckIdException(ownerId, capabilityType, localId, ownerError!);
		}

		if (!MacroDeckId.TryValidateLocalId(localId, localKind, out var localError))
		{
			throw new MacroDeckIdException(ownerId, capabilityType, localId, localError!);
		}

		return new QualifiedId(ownerId, localId);
	}

	/// <summary>
	/// Builds a qualified id, returning <c>false</c> instead of throwing. Accepts either owner kind and
	/// applies the resource local-id rule, which is the permissive combination appropriate for data the
	/// host did not itself declare.
	/// </summary>
	public static bool TryCreate(string? ownerId, string? localId, out QualifiedId id)
		=> TryCreate(ownerId, localId, LocalIdKind.Resource, out id);

	public static bool TryCreate(string? ownerId, string? localId, LocalIdKind localKind, out QualifiedId id)
	{
		if (!MacroDeckId.IsValidOwnerId(ownerId) || !MacroDeckId.IsValidLocalId(localId, localKind))
		{
			id = default;
			return false;
		}

		id = new QualifiedId(ownerId!, localId!);
		return true;
	}

	/// <summary>
	/// Non-throwing creation that also pins the owner kind, for a caller that knows which population
	/// the owner belongs to - a host provider must not pass for a package, or an integration shipping a
	/// bare slug would quietly take a host provider's namespace.
	/// </summary>
	public static bool TryCreate(
		string? ownerId,
		string? localId,
		OwnerIdKind ownerKind,
		LocalIdKind localKind,
		out QualifiedId id)
	{
		if (!MacroDeckId.IsValidOwnerId(ownerId, ownerKind) || !MacroDeckId.IsValidLocalId(localId, localKind))
		{
			id = default;
			return false;
		}

		id = new QualifiedId(ownerId!, localId!);
		return true;
	}

	/// <exception cref="MacroDeckIdException">The value is not a well-formed qualified id.</exception>
	public static QualifiedId Parse(string value)
	{
		if (!TryParse(value, out var id))
		{
			throw new MacroDeckIdException(string.Empty,
				"Capability",
				value,
				$"Not a well-formed '{Separator}'-qualified id.");
		}

		return id;
	}

	/// <summary>
	/// Splits a qualified id at its first separator. Returns <c>false</c> for anything that is not
	/// <c>ownerId::localId</c> with both halves valid, so a caller never resolves against a half-parsed
	/// id - including a nested one such as <c>a::b::c</c>, whose local half would itself be qualified.
	/// </summary>
	public static bool TryParse(string? value, out QualifiedId id)
	{
		id = default;

		if (string.IsNullOrEmpty(value))
		{
			return false;
		}

		var index = value.IndexOf(Separator, StringComparison.Ordinal);
		if (index <= 0 || index >= value.Length - Separator.Length)
		{
			return false;
		}

		return TryCreate(value[..index], value[(index + Separator.Length)..], out id);
	}

	/// <summary>
	/// Whether a string is a well-formed qualified id. Used where the presence of an owner prefix is
	/// itself the discriminator - a virtual profile carries one, a user's own profile is a bare GUID.
	/// </summary>
	public static bool IsQualified(string? value) => TryParse(value, out _);
}
