namespace MacroDeckHost.Application.Store.Operations;

public interface IStoreOperationStore
{
	IReadOnlyList<StoreOperation> LoadAll();

	void SaveAll(IReadOnlyList<StoreOperation> operations);
}
