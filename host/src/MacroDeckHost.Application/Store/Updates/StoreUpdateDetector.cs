using MacroDeck.Plugin.Packaging.Versioning;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store.Updates;

public sealed class StoreUpdateDetector : IStoreUpdateDetector
{
	private readonly IStoreCatalog _catalog;
	private readonly IPluginInstallationCatalog _plugins;
	private readonly IStoreInstallationStore _installations;
	private readonly IStoreUpdateState _state;

	public StoreUpdateDetector(IStoreCatalog catalog,
		IPluginInstallationCatalog plugins,
		IStoreInstallationStore installations,
		IStoreUpdateState state)
	{
		_catalog = catalog;
		_plugins = plugins;
		_installations = installations;
		_state = state;
	}

	public IReadOnlyList<StoreAvailableUpdate> Check()
	{
		var snapshot = _catalog.Snapshot;
		var removed = snapshot.RemovedPackages.Count == 0
			? null
			: snapshot.RemovedPackages.Select(package => package.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

		var updates = new List<StoreAvailableUpdate>();
		foreach (var entry in snapshot.Entries)
		{
			if (removed is not null && removed.Contains(entry.Id))
			{
				continue;
			}

			var installedVersion = InstalledVersion(entry);
			if (installedVersion is null)
			{
				continue;
			}

			// An unparseable version on either side is never reported as an update: offering one that
			// cannot be compared would be a guess, and starting an install off that guess is worse than
			// staying quiet about it.
			if (!SemanticVersion.TryParse(installedVersion, out var installed) ||
				!SemanticVersion.TryParse(entry.LatestVersion, out var latest) ||
				latest.CompareTo(installed) <= 0)
			{
				continue;
			}

			updates.Add(new StoreAvailableUpdate
			{
				Kind = entry.Kind,
				PackageId = entry.Id,
				Name = entry.Name,
				InstalledVersion = installedVersion,
				LatestVersion = entry.LatestVersion
			});
		}

		_state.Swap(updates);
		return updates;
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
}
