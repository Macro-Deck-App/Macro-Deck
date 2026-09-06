namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreFeaturedRef
{
	public required StoreExtensionKind Kind { get; init; }

	public required string Id { get; init; }
}
