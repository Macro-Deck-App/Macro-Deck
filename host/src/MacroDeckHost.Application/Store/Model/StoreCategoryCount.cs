namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreCategoryCount
{
	public required StoreCategory Category { get; init; }

	public required int Count { get; init; }
}
