namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreCatalogPage
{
	public static readonly StoreCatalogPage Empty = new() { Items = [], Total = 0 };

	public required IReadOnlyList<StoreCatalogItem> Items { get; init; }

	/// <summary>Entries matching the query's filters before <see cref="StoreCatalogQuery.Skip"/> and
	/// <see cref="StoreCatalogQuery.Take"/> are applied, so a caller can tell a full page from the end
	/// of the catalog.</summary>
	public required int Total { get; init; }
}
