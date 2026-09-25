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
	private readonly IStoreWithdrawalState _withdrawals;
	private readonly StoreRegistryOptions _options;

	public StoreUpdateDetector(IStoreCatalog catalog,
		IPluginInstallationCatalog plugins,
		IStoreInstallationStore installations,
		IStoreUpdateState state,
		IStoreWithdrawalState withdrawals,
		StoreRegistryOptions options)
	{
		_catalog = catalog;
		_plugins = plugins;
		_installations = installations;
		_state = state;
		_withdrawals = withdrawals;
		_options = options;
	}

	public IReadOnlyList<StoreAvailableUpdate> Check()
	{
		var snapshot = _catalog.Snapshot;
		var plugins = _plugins.Discover();
		var updates = new List<StoreAvailableUpdate>();
		var withdrawals = new List<StoreInstalledWithdrawal>();
		foreach (var entry in snapshot.Entries)
		{
			var installedVersion = InstalledVersion(plugins, entry.Kind, entry.Id);
			if (installedVersion is null)
			{
				continue;
			}

			if (snapshot.FindRemoval(entry.Id, installedVersion) is { } removal)
			{
				withdrawals.Add(Withdrawal(entry.Kind, entry.Id, entry.Name, installedVersion, removal, listed: true));
			}

			if (snapshot.FindWithdrawal(entry) is not null)
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

		// An empty snapshot says nothing about removals, so it must not clear warnings a loaded one raised.
		if (snapshot.Entries.Count > 0 || snapshot.Sequence > 0)
		{
			withdrawals.AddRange(UnlistedWithdrawals(snapshot, plugins));
			_withdrawals.Swap(withdrawals);
		}

		_state.Swap(updates);
		return updates;
	}

	private IEnumerable<StoreInstalledWithdrawal> UnlistedWithdrawals(StoreCatalogSnapshot snapshot,
		IReadOnlyList<InstalledPlugin> plugins)
	{
		foreach (var record in _installations.LoadAll())
		{
			if (record.Kind is not (StoreExtensionKind.Plugin or StoreExtensionKind.IconPack) ||
				!SameOrigin(record.Origin) ||
				snapshot.Entries.Any(entry =>
					entry.Kind == record.Kind && string.Equals(entry.Id, record.PackageId, StringComparison.OrdinalIgnoreCase)))
			{
				continue;
			}

			var installedVersion = record.Kind is StoreExtensionKind.Plugin
				? InstalledVersion(plugins, record.Kind, record.PackageId)
				: record.Version;
			if (installedVersion is not null && snapshot.FindRemoval(record.PackageId, installedVersion) is { } removal)
			{
				yield return Withdrawal(record.Kind,
					record.PackageId,
					record.DisplayName ?? record.PackageId,
					installedVersion,
					removal,
					listed: false);
			}
		}
	}

	private bool SameOrigin(string origin) =>
		Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
		string.Equals(uri.GetLeftPart(UriPartial.Path), _options.Origin, StringComparison.OrdinalIgnoreCase);

	private static StoreInstalledWithdrawal Withdrawal(StoreExtensionKind kind,
		string packageId,
		string name,
		string installedVersion,
		StoreRemovedPackage removal,
		bool listed) => new()
	{
		Kind = kind,
		PackageId = packageId,
		Name = name,
		InstalledVersion = installedVersion,
		Reason = removal.Reason,
		Replacement = removal.Replacement,
		Listed = listed
	};

	private string? InstalledVersion(IReadOnlyList<InstalledPlugin> plugins, StoreExtensionKind kind, string id)
	{
		if (kind is StoreExtensionKind.Plugin)
		{
			return plugins
				.FirstOrDefault(plugin => string.Equals(plugin.PluginId, id, StringComparison.OrdinalIgnoreCase))
				?.ActiveVersion?.Version;
		}

		return _installations.Find(kind, id)?.Version;
	}
}
