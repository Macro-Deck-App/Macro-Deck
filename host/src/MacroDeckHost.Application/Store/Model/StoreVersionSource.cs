namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreVersionSource
{
	public string? Commit { get; init; }

	public string? Tag { get; init; }
}
