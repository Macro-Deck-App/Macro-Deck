using MacroDeckHost.Application.Store.Model;

namespace MacroDeckHost.Application.Store;

public interface IStoreCatalog
{
	StoreCatalogSnapshot Snapshot { get; }

	void Swap(StoreCatalogSnapshot snapshot);
}
