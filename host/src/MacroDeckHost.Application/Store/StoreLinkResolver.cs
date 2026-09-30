using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store;

public enum StoreLinkOutcome
{
	Found,
	NotFound,
	RegistryUnavailable
}

public sealed record StoreLinkResolution(StoreLinkOutcome Outcome, StoreExtensionKind Kind = default, string? Id = null);

public interface IStoreLinkResolver
{
	StoreLinkResolution Resolve(string? packageId);
}

// A link carries only a package id, looked up by exact match in the official listing. Unlike Find, a
// withdrawn package is hidden even when installed, and an id shared by two kinds is not resolved.
public sealed class StoreLinkResolver : IStoreLinkResolver
{
	public const int MaxPackageIdLength = 128;

	private static readonly StoreLinkResolution _notFound = new(StoreLinkOutcome.NotFound);

	private readonly IStoreCatalog _catalog;
	private readonly StoreRegistryOptions _options;

	public StoreLinkResolver(IStoreCatalog catalog, StoreRegistryOptions options)
	{
		_catalog = catalog;
		_options = options;
	}

	public StoreLinkResolution Resolve(string? packageId)
	{
		if (!_options.IsOfficialRegistry || string.IsNullOrEmpty(packageId) || packageId.Length > MaxPackageIdLength)
		{
			return _notFound;
		}

		var snapshot = _catalog.Snapshot;
		if (snapshot.Entries.Count == 0 && snapshot.Sequence == 0)
		{
			return new StoreLinkResolution(StoreLinkOutcome.RegistryUnavailable);
		}

		var matches = snapshot.Entries
			.Where(candidate => string.Equals(candidate.Id, packageId, StringComparison.Ordinal) &&
				snapshot.FindWithdrawal(candidate) is null)
			.Take(2)
			.ToList();

		return matches is [var entry]
			? new StoreLinkResolution(StoreLinkOutcome.Found, entry.Kind, entry.Id)
			: _notFound;
	}
}
