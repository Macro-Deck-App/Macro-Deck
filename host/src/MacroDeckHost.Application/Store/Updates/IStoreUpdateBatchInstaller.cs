using MacroDeckHost.Application.Store.Operations;

namespace MacroDeckHost.Application.Store.Updates;

public interface IStoreUpdateBatchInstaller
{
	IReadOnlyList<StoreOperation> Install(IEnumerable<StoreAvailableUpdate> updates, string? backupBatchId = null);
}
