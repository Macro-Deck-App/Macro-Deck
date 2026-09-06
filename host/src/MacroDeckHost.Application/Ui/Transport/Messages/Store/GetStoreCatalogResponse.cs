namespace MacroDeckHost.Application.Ui.Transport.Messages.Store;

public class GetStoreCatalogResponse
{
	public List<StoreCatalogItemBody> Items { get; set; } = [];

	/// <summary>Entries matching the query before <c>skip</c>/<c>take</c>, so a client can tell a full
	/// page from the end of the catalog.</summary>
	public int Total { get; set; }

	public StoreRegistryStatusBody Registry { get; set; } = new();
}
