using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store;

public sealed class StoreCatalog : IStoreCatalog
{
	private volatile StoreCatalogSnapshot _snapshot = StoreCatalogSnapshot.Empty;

	public StoreCatalogSnapshot Snapshot => _snapshot;

	public void Swap(StoreCatalogSnapshot snapshot)
	{
		ArgumentNullException.ThrowIfNull(snapshot);
		_snapshot = snapshot;
	}
}
