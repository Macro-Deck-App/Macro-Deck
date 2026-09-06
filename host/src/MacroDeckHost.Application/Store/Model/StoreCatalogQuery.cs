namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreCatalogQuery
{
	public const int MaxTake = 100;

	public IReadOnlyList<StoreExtensionKind>? Kinds { get; init; }

	public string? Search { get; init; }

	public StoreCatalogSection Section { get; init; } = StoreCatalogSection.All;

	public int Skip { get; init; }

	public int Take { get; init; } = MaxTake;
}
