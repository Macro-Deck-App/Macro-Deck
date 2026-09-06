using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Application.Store.Updates;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Store.Installation;

public sealed class StoreIconPackOwner : IIconPackOwner
{
	private readonly IStoreInstallationStore _installations;
	private readonly IStoreUpdateDetector _updateDetector;
	private readonly ILogger _logger;

	public StoreIconPackOwner(IStoreInstallationStore installations,
		IStoreUpdateDetector updateDetector,
		ILogger logger)
	{
		_installations = installations;
		_updateDetector = updateDetector;
		_logger = logger;
	}

	// Ownership is claimed against a live installation record, not the entity's own SourceType/SourceId
	// stamp: a pack whose record is gone (a backup restored without data/store, or a crash between
	// AddOrUpdatePack and the installation save in StoreInstallExecutor) would otherwise be tagged
	// "Store" while the Store reports it not installed, and would be undeletable-by-delegation with no
	// record to delete.
	public bool Owns(IconPackEntity pack) => FindRecord(pack) is not null;

	public IconPackOwnerDescriptor Describe(IconPackEntity pack) => new(IconPackOwnerKind.Store, true);

	public Task Release(IconPackEntity pack)
	{
		_installations.Delete(StoreExtensionKind.IconPack, pack.SourceId!);

		// Deliberately not part of this method's "both or neither" contract: Check() only refreshes
		// the in-memory update-state cache from the catalog snapshot and installation records, so a
		// failure here cannot leave the pack/record pair out of sync - it would just delay the
		// "update available" badge clearing until the next registry refresh.
		try
		{
			_updateDetector.Check();
		}
		catch (Exception ex)
		{
			_logger.Warning(ex, "Failed to refresh update state after releasing icon pack {SourceId}", pack.SourceId);
		}

		return Task.CompletedTask;
	}

	private StoreInstallationRecord? FindRecord(IconPackEntity pack)
	{
		if (pack.SourceType != IconPackSourceType.ExtensionStore || string.IsNullOrEmpty(pack.SourceId))
		{
			return null;
		}

		var record = _installations.Find(StoreExtensionKind.IconPack, pack.SourceId);
		return record is not null && record.TargetIds.Contains(pack.Id) ? record : null;
	}
}
