using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Application.Store;

public interface IStoreCatalogQueryService
{
	Result<StoreCatalogPage, StoreCatalogError> Query(StoreCatalogQuery query);

	Result<StoreCatalogItem, StoreCatalogError> Find(StoreExtensionKind kind, string id);

	IReadOnlyList<StoreCatalogItem> Installed();
}
