namespace MacroDeckHost.Application.Store.Model;

public sealed record StoreVersionHistoryEntry
{
	public required string Version { get; init; }

	public DateTimeOffset? ReleasedAt { get; init; }

	public string? Changelog { get; init; }
}
