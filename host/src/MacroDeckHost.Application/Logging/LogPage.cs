namespace MacroDeckHost.Application.Logging;

public sealed record LogPage
{
	public required IReadOnlyList<LogEntry> Entries { get; init; }

	public LogCursor? Older { get; init; }

	public required LogCursor TailAnchor { get; init; }
}

public sealed record LogTailBatch
{
	public required IReadOnlyList<LogEntry> Entries { get; init; }

	public required LogCursor Position { get; init; }
}

public sealed record LogSourceSummary
{
	public required LogEntrySource Source { get; init; }

	public string? SourceId { get; init; }
}
