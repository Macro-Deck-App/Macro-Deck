namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreCategory
{
	public required string Id { get; init; }

	public required IReadOnlyDictionary<string, string> Names { get; init; }
}
