namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreCatalogEntry
{
	public required StoreExtensionKind Kind { get; init; }

	public required string Id { get; init; }

	public required string Name { get; init; }

	public required string LatestVersion { get; init; }

	public string? Description { get; init; }

	public string? Publisher { get; init; }

	public string? Repository { get; init; }

	public string? License { get; init; }

	public DateTimeOffset? CreatedAt { get; init; }

	public DateTimeOffset? UpdatedAt { get; init; }

	public IReadOnlyList<string> SupportedRids { get; init; } = [];

	/// <summary>BCP-47 tags naming the languages this package's own user-facing strings ship in, as its
	/// manifest declares them. Empty means the registry says nothing - never that the package is
	/// English-only.</summary>
	public IReadOnlyList<string> Languages { get; init; } = [];

	public required StoreReleaseManifest LatestRelease { get; init; }

	public string? Changelog { get; init; }

	public string? LongDescription { get; init; }

	public IReadOnlyList<StoreVersionHistoryEntry> History { get; init; } = [];
}
