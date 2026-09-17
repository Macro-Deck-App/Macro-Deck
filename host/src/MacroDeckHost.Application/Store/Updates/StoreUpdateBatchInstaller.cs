using MacroDeckHost.Application.Store.Operations;

namespace MacroDeckHost.Application.Store.Updates;

public sealed class StoreUpdateBatchInstaller : IStoreUpdateBatchInstaller
{
	private readonly IStoreInstallCoordinator _coordinator;

	public StoreUpdateBatchInstaller(IStoreInstallCoordinator coordinator)
	{
		_coordinator = coordinator;
	}

	public IReadOnlyList<StoreOperation> Install(IEnumerable<StoreAvailableUpdate> updates, string? backupBatchId = null)
	{
		var batchId = backupBatchId ?? Guid.NewGuid().ToString("N");
		return updates
			.Where(update => StoreUpdateScope.IsUpdatable(update.Kind))
			.Select(update => _coordinator.Install(update.Kind, update.PackageId, backupBatchId: batchId))
			.ToList();
	}
}
