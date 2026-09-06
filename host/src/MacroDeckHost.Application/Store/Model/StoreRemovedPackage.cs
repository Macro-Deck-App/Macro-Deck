namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreRemovedPackage
{
	public required string Id { get; init; }

	public string? Version { get; init; }

	public string? Reason { get; init; }

	public string? Replacement { get; init; }
}
