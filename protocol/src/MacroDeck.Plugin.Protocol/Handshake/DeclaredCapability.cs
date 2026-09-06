using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Protocol.Handshake;

/// <summary>
/// One capability a plugin declares. Declaration data only - deliberately carries no availability
/// field. ADR 0004's declared/registered/available split means availability is the host's to decide,
/// never the plugin's to assert.
/// </summary>
public sealed record DeclaredCapability
{
	/// <summary>One of <see cref="CapabilityKinds" />.</summary>
	public required string Kind { get; init; }

	/// <summary>The capability's own local id, validated the same way an in-process integration's is.</summary>
	public required string LocalId { get; init; }

	public required CapabilityVersionRange VersionRange { get; init; }

	public string? DisplayName { get; init; }
}
