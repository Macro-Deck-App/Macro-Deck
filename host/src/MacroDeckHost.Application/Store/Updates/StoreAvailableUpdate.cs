using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store.Updates;

public sealed record StoreAvailableUpdate
{
	public required StoreExtensionKind Kind { get; init; }

	public required string PackageId { get; init; }

	public required string Name { get; init; }

	public required string InstalledVersion { get; init; }

	public required string LatestVersion { get; init; }
}
