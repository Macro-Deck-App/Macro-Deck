namespace MacroDeckHost.Application.Logging;

public sealed record LogEntry
{
	public required string Id { get; init; }

	public required DateTimeOffset Timestamp { get; init; }

	public required LogEntryLevel Level { get; init; }

	public required LogEntrySource Source { get; init; }

	public string? SourceId { get; init; }

	public string? Category { get; init; }

	public required string Message { get; init; }

	public string? Exception { get; init; }
}
