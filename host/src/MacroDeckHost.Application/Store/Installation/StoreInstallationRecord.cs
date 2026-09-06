using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store.Installation;

// Icon packs and profile templates have no version or provenance of their own, so the store keeps its
// own record of what it installed. Keyed by origin as well as package id, so a custom registry added
// later cannot be confused with the official one just because an id matches.
public sealed record StoreInstallationRecord
{
	public required string Origin { get; init; }

	public required StoreExtensionKind Kind { get; init; }

	public required string PackageId { get; init; }

	public required string Version { get; init; }

	public string? ArtifactSha256 { get; init; }

	public string? DisplayName { get; init; }

	public long RegistrySequence { get; init; }

	public DateTimeOffset InstalledAt { get; init; }

	public IReadOnlyList<Guid> TargetIds { get; init; } = [];
}
