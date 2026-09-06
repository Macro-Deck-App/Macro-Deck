namespace MacroDeckHost.Application.Logging;

public sealed record LogQuery
{
	public static LogQuery All { get; } = new();

	public IReadOnlyList<LogEntryLevel>? Levels { get; init; }

	public LogEntrySource? Source { get; init; }

	public string? IntegrationId { get; init; }

	public string? Category { get; init; }

	public string? Search { get; init; }

	public DateTimeOffset? From { get; init; }

	public DateTimeOffset? To { get; init; }

	public bool Matches(LogEntry entry, LogEntryLevel bootstrapperMinimum)
	{
		ArgumentNullException.ThrowIfNull(entry);

		if (entry.Source == LogEntrySource.Bootstrapper && entry.Level < bootstrapperMinimum)
		{
			return false;
		}

		if (!InRange(entry))
		{
			return false;
		}

		if (Levels is { Count: > 0 } levels && !levels.Contains(entry.Level))
		{
			return false;
		}

		if (Source is { } source && entry.Source != source)
		{
			return false;
		}

		if (!string.IsNullOrEmpty(IntegrationId) &&
			(entry.Source != LogEntrySource.Integration ||
				!string.Equals(entry.SourceId, IntegrationId, StringComparison.Ordinal)))
		{
			return false;
		}

		if (!string.IsNullOrEmpty(Category) &&
			!string.Equals(entry.Category, Category, StringComparison.Ordinal))
		{
			return false;
		}

		return string.IsNullOrEmpty(Search) || MatchesSearch(entry, Search);
	}

	public bool InRange(LogEntry entry)
	{
		ArgumentNullException.ThrowIfNull(entry);

		return (From is not { } from || entry.Timestamp >= from) && (To is not { } to || entry.Timestamp <= to);
	}

	private static bool MatchesSearch(LogEntry entry, string search)
		=> entry.Message.Contains(search, StringComparison.OrdinalIgnoreCase) ||
			entry.Category?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
			entry.SourceId?.Contains(search, StringComparison.OrdinalIgnoreCase) == true ||
			entry.Exception?.Contains(search, StringComparison.OrdinalIgnoreCase) == true;
}
