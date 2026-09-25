namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreCatalogSnapshot
{
	public static readonly StoreCatalogSnapshot Empty = new()
	{
		Sequence = 0,
		Entries = []
	};

	public required long Sequence { get; init; }

	public required IReadOnlyList<StoreCatalogEntry> Entries { get; init; }

	public DateTimeOffset? GeneratedAt { get; init; }

	public DateTimeOffset? SignedAt { get; init; }

	public DateTimeOffset? FetchedAt { get; init; }

	public IReadOnlyList<StoreRemovedPackage> RemovedPackages { get; init; } = [];

	public IReadOnlyList<string> RevokedKeyIds { get; init; } = [];

	/// <summary>The registry's curated picks, in the order it published them. Empty when the registry
	/// publishes none, which is what every consumer must treat as "nothing is featured".</summary>
	public IReadOnlyList<StoreFeaturedRef> Featured { get; init; } = [];

	public IReadOnlyList<StoreCategory> Categories { get; init; } = [];

	// The registry requires a version on every entry, so one without is malformed and fails closed as
	// covering every version of the package.
	public StoreRemovedPackage? FindRemoval(string id, string? version) =>
		RemovedPackages.FirstOrDefault(removed =>
			string.Equals(removed.Id, id, StringComparison.OrdinalIgnoreCase) &&
			(string.IsNullOrWhiteSpace(removed.Version) ||
				(version is not null && StoreVersions.Same(removed.Version, version))));

	public StoreRemovedPackage? FindWithdrawal(StoreCatalogEntry entry) => FindRemoval(entry.Id, entry.LatestVersion);

	public bool HasRemoval(string id) =>
		RemovedPackages.Any(removed => string.Equals(removed.Id, id, StringComparison.OrdinalIgnoreCase));
}
