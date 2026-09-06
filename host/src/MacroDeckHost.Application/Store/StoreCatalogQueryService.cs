using MacroDeck.Plugin.Packaging.Versioning;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Store;

public sealed class StoreCatalogQueryService : IStoreCatalogQueryService
{
	private readonly IStoreCatalog _catalog;
	private readonly IPluginInstallationCatalog _plugins;
	private readonly IStoreInstallationStore _installations;

	public StoreCatalogQueryService(IStoreCatalog catalog,
		IPluginInstallationCatalog plugins,
		IStoreInstallationStore installations)
	{
		_catalog = catalog;
		_plugins = plugins;
		_installations = installations;
	}

	public Result<StoreCatalogPage, StoreCatalogError> Query(StoreCatalogQuery query)
	{
		ArgumentNullException.ThrowIfNull(query);

		var snapshot = _catalog.Snapshot;
		if (snapshot.Entries.Count == 0 && snapshot.Sequence == 0)
		{
			return Result.Fail<StoreCatalogPage, StoreCatalogError>(StoreCatalogError.RegistryUnavailable);
		}

		var entries = Visible(snapshot).AsEnumerable();
		if (query.Kinds is { Count: > 0 })
		{
			entries = entries.Where(entry => query.Kinds.Contains(entry.Kind));
		}

		Dictionary<string, int>? featured = null;
		if (query.Section is StoreCatalogSection.Featured)
		{
			featured = FeaturedPositions(snapshot);
			entries = entries.Where(entry => featured.ContainsKey(FeaturedKey(entry.Kind, entry.Id)));
		}

		var term = string.IsNullOrWhiteSpace(query.Search) ? null : query.Search.Trim();
		if (term is not null)
		{
			entries = entries.Where(entry => Matches(entry, term));
		}

		var matches = entries.ToList();
		var take = Math.Clamp(query.Take, 1, StoreCatalogQuery.MaxTake);
		var items = Order(matches, query.Section, term, featured)
			.Skip(Math.Max(0, query.Skip))
			.Take(take)
			.Select(Describe)
			.ToList();

		return Result.Ok<StoreCatalogPage, StoreCatalogError>(new StoreCatalogPage
		{
			Items = items,
			Total = matches.Count
		});
	}

	// Every ordering ends in the same total-order tiebreak: entries that tie on the requested key must
	// still fall in a fixed order, or paging repeats one row on the next page and drops another.
	private static IEnumerable<StoreCatalogEntry> Order(List<StoreCatalogEntry> matches,
		StoreCatalogSection section,
		string? term,
		Dictionary<string, int>? featured) =>
		(section switch
		{
			StoreCatalogSection.Newest => matches.OrderByDescending(entry => entry.CreatedAt),
			StoreCatalogSection.RecentlyUpdated => matches.OrderByDescending(entry => entry.UpdatedAt),
			StoreCatalogSection.Featured =>
				matches.OrderBy(entry => featured![FeaturedKey(entry.Kind, entry.Id)]),
			StoreCatalogSection.Name => matches.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase),
			_ => term is null
				? matches.OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
				: matches.OrderBy(entry => Rank(entry, term))
		})
		.ThenBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
		.ThenBy(entry => entry.Kind)
		.ThenBy(entry => entry.Id, StringComparer.Ordinal);

	private static Dictionary<string, int> FeaturedPositions(StoreCatalogSnapshot snapshot)
	{
		var positions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
		for (var index = 0; index < snapshot.Featured.Count; index++)
		{
			var reference = snapshot.Featured[index];
			positions.TryAdd(FeaturedKey(reference.Kind, reference.Id), index);
		}

		return positions;
	}

	private static string FeaturedKey(StoreExtensionKind kind, string id) => $"{kind}:{id}";

	public Result<StoreCatalogItem, StoreCatalogError> Find(StoreExtensionKind kind, string id)
	{
		var snapshot = _catalog.Snapshot;
		if (snapshot.Entries.Count == 0 && snapshot.Sequence == 0)
		{
			return Result.Fail<StoreCatalogItem, StoreCatalogError>(StoreCatalogError.RegistryUnavailable);
		}

		var entry = Visible(snapshot)
			.FirstOrDefault(candidate =>
				candidate.Kind == kind && string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));

		return entry is null
			? Result.Fail<StoreCatalogItem, StoreCatalogError>(StoreCatalogError.NotFound)
			: Result.Ok<StoreCatalogItem, StoreCatalogError>(Describe(entry));
	}

	public IReadOnlyList<StoreCatalogItem> Installed() =>
		Visible(_catalog.Snapshot)
			.Select(Describe)
			.Where(item => item.InstallState is StoreInstallState.Installed or StoreInstallState.UpdateAvailable)
			.ToList();

	private static IReadOnlyList<StoreCatalogEntry> Visible(StoreCatalogSnapshot snapshot)
	{
		if (snapshot.RemovedPackages.Count == 0)
		{
			return snapshot.Entries;
		}

		var removed = snapshot.RemovedPackages
			.Select(package => package.Id)
			.ToHashSet(StringComparer.OrdinalIgnoreCase);

		return snapshot.Entries.Where(entry => !removed.Contains(entry.Id)).ToList();
	}

	private StoreCatalogItem Describe(StoreCatalogEntry entry)
	{
		var installedVersion = InstalledVersion(entry);
		var unsupportedReason = UnsupportedReason(entry);
		var state = unsupportedReason is not null
			? StoreInstallState.Unsupported
			: installedVersion is null
				? StoreInstallState.NotInstalled
				: HasUpdate(installedVersion, entry.LatestVersion)
					? StoreInstallState.UpdateAvailable
					: StoreInstallState.Installed;

		return new StoreCatalogItem
		{
			Entry = entry,
			InstallState = state,
			InstalledVersion = installedVersion,
			UnsupportedReason = unsupportedReason
		};
	}

	private string? InstalledVersion(StoreCatalogEntry entry)
	{
		if (entry.Kind is StoreExtensionKind.Plugin)
		{
			return _plugins.Discover()
				.FirstOrDefault(plugin =>
					string.Equals(plugin.PluginId, entry.Id, StringComparison.OrdinalIgnoreCase))
				?.ActiveVersion?.Version;
		}

		return _installations.Find(entry.Kind, entry.Id)?.Version;
	}

	private static string? UnsupportedReason(StoreCatalogEntry entry)
	{
		if (entry.SupportedRids.Count == 0)
		{
			return null;
		}

		var current = System.Runtime.InteropServices.RuntimeInformation.RuntimeIdentifier;
		return entry.SupportedRids.Contains(current, StringComparer.OrdinalIgnoreCase)
			? null
			: $"This extension does not support {current}.";
	}

	// An unparseable version on either side is never reported as an update: offering one that cannot be
	// compared would be a guess, and the install button is destructive enough to warrant staying quiet.
	private static bool HasUpdate(string installed, string latest) =>
		SemanticVersion.TryParse(installed, out var installedVersion) &&
		SemanticVersion.TryParse(latest, out var latestVersion) &&
		latestVersion > installedVersion;

	private static bool Matches(StoreCatalogEntry entry, string term) =>
		Contains(entry.Name, term) ||
		Contains(entry.Id, term) ||
		Contains(entry.Description, term) ||
		Contains(entry.Publisher, term);

	private static bool Contains(string? value, string term) =>
		value is not null && value.Contains(term, StringComparison.OrdinalIgnoreCase);

	private static int Rank(StoreCatalogEntry entry, string term)
	{
		if (string.Equals(entry.Id, term, StringComparison.OrdinalIgnoreCase))
		{
			return 0;
		}

		if (entry.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
		{
			return 1;
		}

		return Contains(entry.Name, term) ? 2 : Contains(entry.Publisher, term) ? 3 : 4;
	}
}
