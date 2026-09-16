using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store.Reviews;

public interface IStoreOfficialPackages
{
	bool CatalogLoaded { get; }

	string? ResolveListed(StoreExtensionKind kind, string id);

	IReadOnlyList<string> ListedPackageIds(IEnumerable<string> ids);

	bool IsInstalled(string packageId);

	IReadOnlyList<string> InstalledPackageIds();
}

public sealed class StoreOfficialPackages : IStoreOfficialPackages
{
	private readonly IStoreCatalog _catalog;
	private readonly IStoreCatalogQueryService _catalogQuery;
	private readonly IStoreInstallationStore _installations;
	private readonly StoreRegistryOptions _options;

	public StoreOfficialPackages(IStoreCatalog catalog,
		IStoreCatalogQueryService catalogQuery,
		IStoreInstallationStore installations,
		StoreRegistryOptions options)
	{
		_catalog = catalog;
		_catalogQuery = catalogQuery;
		_installations = installations;
		_options = options;
	}

	public bool CatalogLoaded => _catalog.Snapshot is { } snapshot && (snapshot.Entries.Count > 0 || snapshot.Sequence > 0);

	public string? ResolveListed(StoreExtensionKind kind, string id)
	{
		if (!_options.IsOfficialRegistry || string.IsNullOrWhiteSpace(id))
		{
			return null;
		}

		var found = _catalogQuery.Find(kind, id);
		return found.Success ? found.Data!.Entry.Id : null;
	}

	public IReadOnlyList<string> ListedPackageIds(IEnumerable<string> ids)
	{
		if (!_options.IsOfficialRegistry)
		{
			return [];
		}

		var listed = ListedIds();
		return ids
			.Select(id => listed.TryGetValue(id.Trim(), out var canonical) ? canonical : null)
			.OfType<string>()
			.Distinct(StringComparer.Ordinal)
			.ToList();
	}

	public bool IsInstalled(string packageId) =>
		InstalledPackageIds().Contains(packageId, StringComparer.OrdinalIgnoreCase);

	// Plugins carry no installation record, so a plugin counts only while the official catalog lists it.
	// Icon packs and templates also count from their own official-origin record once unlisted.
	public IReadOnlyList<string> InstalledPackageIds()
	{
		if (!_options.IsOfficialRegistry)
		{
			return [];
		}

		var ids = new List<string>();
		foreach (var item in _catalogQuery.Installed())
		{
			if (item.Entry.Kind is StoreExtensionKind.Plugin ||
				StoreRegistryOptions.IsOfficial(_installations.Find(item.Entry.Kind, item.Entry.Id)?.Origin))
			{
				ids.Add(item.Entry.Id);
			}
		}

		ids.AddRange(_installations.LoadAll()
			.Where(record => StoreRegistryOptions.IsOfficial(record.Origin))
			.Select(record => record.PackageId));

		return ids.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
	}

	private Dictionary<string, string> ListedIds()
	{
		var snapshot = _catalog.Snapshot;
		var removed = snapshot.RemovedPackages.Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
		var listed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var entry in snapshot.Entries.Where(entry => !removed.Contains(entry.Id)))
		{
			listed.TryAdd(entry.Id, entry.Id);
		}

		return listed;
	}
}
