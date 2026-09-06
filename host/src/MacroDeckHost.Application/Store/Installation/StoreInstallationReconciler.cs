using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store.Installation;

public sealed class StoreInstallationReconciler : IStoreInstallationReconciler
{
	private readonly IStoreInstallationStore _installations;
	private readonly IIconPackCache _iconPackCache;

	public StoreInstallationReconciler(IStoreInstallationStore installations, IIconPackCache iconPackCache)
	{
		_installations = installations;
		_iconPackCache = iconPackCache;
	}

	public void PruneOrphanedIconPackRecords()
	{
		foreach (var record in _installations.LoadAll())
		{
			if (record.Kind != StoreExtensionKind.IconPack)
			{
				continue;
			}

			if (record.TargetIds.Any(id => _iconPackCache.GetPackById(id) is not null))
			{
				continue;
			}

			_installations.Delete(record.Kind, record.PackageId);
		}
	}
}
