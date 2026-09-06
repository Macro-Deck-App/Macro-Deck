using System.Globalization;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;

namespace MacroDeckHost.Infrastructure.Logging;

public sealed class LogFileReader : ILogFileReader
{
	public const int DefaultPageSize = 200;
	public const int MaxPageSize = 500;

	public const int MaxScannedEntries = 5000;

	public const int MaxSkippedEntries = 50_000;

	private const int SourceScanLimit = 5000;

	private static readonly TimeSpan _sourceScanWindow = TimeSpan.FromDays(7);

	private readonly ILogLevelState _logLevelState;
	private readonly LogFileSet _host;
	private readonly LogFileSet _bootstrapper;

	private readonly LogFileTailReader _hostTail;
	private readonly LogFileTailReader _bootstrapperTail;

	public LogFileReader(IMacroDeckPaths paths, ILogLevelState logLevelState)
	{
		ArgumentNullException.ThrowIfNull(paths);

		_logLevelState = logLevelState;
		_host = new LogFileSet(paths.LogsDirectory, LogFileKind.Host);
		_bootstrapper = new LogFileSet(paths.LogsDirectory, LogFileKind.Bootstrapper);
		_hostTail = new LogFileTailReader(_host);
		_bootstrapperTail = new LogFileTailReader(_bootstrapper);
	}

	public LogPage ReadPage(LogQuery query, LogCursor? before, int limit)
	{
		ArgumentNullException.ThrowIfNull(query);

		var anchor = CurrentEnd();
		var pageSize = Math.Clamp(limit <= 0 ? DefaultPageSize : limit, 1, MaxPageSize);
		var minimum = _logLevelState.Minimum;
		var start = before ?? (query.To is { } to ? NewestAtOrBefore(to) : anchor);
		var floor = query.From is { } from ? DayStampBelow(from) : null;

		var streams = new[]
		{
			new StreamScan(_host, start.Host, floor),
			new StreamScan(_bootstrapper, start.Bootstrapper, floor)
		};

		var matches = new List<LogEntry>(pageSize);
		var scanned = 0;
		var skipped = 0;
		var older = start;
		var exhausted = false;

		while (matches.Count < pageSize && scanned < MaxScannedEntries && skipped < MaxSkippedEntries)
		{
			var next = Newest(streams);
			if (next is null)
			{
				exhausted = true;
				break;
			}

			var (stream, entry) = next.Value;
			older = older.With(stream, PositionOf(entry));

			// An entry outside the requested range is skipped but never ends the scan: file timestamps
			// are not monotonic - the files are read while other threads append to them - so a single
			// out-of-order line must not freeze paging. Only a day file that lies entirely below the
			// range terminates the scan, and it does so with a day of slack (see StreamScan).
			if (!query.InRange(entry))
			{
				skipped++;

				continue;
			}

			scanned++;

			if (query.Matches(entry, minimum))
			{
				matches.Add(entry);
			}
		}

		if (!exhausted && Array.TrueForAll(streams, stream => stream.Peek() is null))
		{
			exhausted = true;
		}

		matches.Reverse();

		return new LogPage
		{
			Entries = matches,
			Older = exhausted ? null : older,
			TailAnchor = anchor
		};
	}

	public LogTailBatch ReadAfter(LogCursor position)
	{
		var (hostEntries, hostPosition) = _hostTail.Read(position.Host);
		var (bootEntries, bootPosition) = _bootstrapperTail.Read(position.Bootstrapper);

		var entries = new List<LogEntry>(hostEntries.Count + bootEntries.Count);
		entries.AddRange(hostEntries);
		entries.AddRange(bootEntries);
		entries.Sort(static (left, right) => left.Timestamp.CompareTo(right.Timestamp));

		return new LogTailBatch
		{
			Entries = entries,
			Position = new LogCursor(hostPosition, bootPosition)
		};
	}

	public LogCursor CurrentEnd() => new(EndOf(_host), EndOf(_bootstrapper));

	public IReadOnlyList<LogSourceSummary> ReadSources()
	{
		var minimum = _logLevelState.Minimum;
		var keys = new HashSet<(LogEntrySource Source, string? SourceId)>();

		var bootstrapperScanned = 0;
		foreach (var (entry, _) in new ReverseLogEntryReader(_bootstrapper).Read(EndOf(_bootstrapper)))
		{
			if (++bootstrapperScanned > SourceScanLimit)
			{
				break;
			}

			if (entry.Level < minimum)
			{
				continue;
			}

			keys.Add((entry.Source, null));

			break;
		}

		var scanned = 0;
		DateTimeOffset? newest = null;
		foreach (var (entry, _) in new ReverseLogEntryReader(_host).Read(EndOf(_host)))
		{
			if (++scanned > SourceScanLimit)
			{
				break;
			}

			newest ??= entry.Timestamp;
			if (newest - entry.Timestamp > _sourceScanWindow)
			{
				break;
			}

			keys.Add((entry.Source, entry.Source == LogEntrySource.Integration ? entry.SourceId : null));
		}

		return keys
			.Select(key => new LogSourceSummary { Source = key.Source, SourceId = key.SourceId })
			.OrderBy(summary => summary.Source)
			.ThenBy(summary => summary.SourceId, StringComparer.Ordinal)
			.ToList();
	}

	private static LogReadPosition PositionOf(LogEntry entry)
		=> LogEntryId.TryParse(entry.Id, out _, out var position) ? position : LogReadPosition.None;

	private LogCursor NewestAtOrBefore(DateTimeOffset to)
		=> new(NewestAtOrBefore(_host, to), NewestAtOrBefore(_bootstrapper, to));

	// File stamps are local dates taken when the line was written, so a day boundary crossed by a DST
	// shift or by a travelling machine can put an in-range entry in the next day's file.
	private static LogReadPosition NewestAtOrBefore(LogFileSet set, DateTimeOffset to)
	{
		var ceiling = DayStampAbove(to);
		foreach (var stamp in set.Stamps())
		{
			if (string.CompareOrdinal(stamp, ceiling) > 0)
			{
				continue;
			}

			try
			{
				return new LogReadPosition(stamp, ReverseLineReader.EndOfLastCompleteLine(set.PathFor(stamp)));
			}
			catch (IOException)
			{
				// The files are shared-write, so one of them being momentarily unreadable must cost the
				// query that day, not the whole stream.
				continue;
			}
			catch (UnauthorizedAccessException)
			{
				continue;
			}
		}

		return LogReadPosition.None;
	}

	private static string DayStampAbove(DateTimeOffset instant)
		=> instant.ToLocalTime().Date.AddDays(1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	private static string DayStampBelow(DateTimeOffset instant)
		=> instant.ToLocalTime().Date.AddDays(-1).ToString("yyyyMMdd", CultureInfo.InvariantCulture);

	private static LogReadPosition EndOf(LogFileSet set)
	{
		var stamp = set.NewestStamp();
		if (stamp is null)
		{
			return LogReadPosition.None;
		}

		try
		{
			return new LogReadPosition(stamp, ReverseLineReader.EndOfLastCompleteLine(set.PathFor(stamp)));
		}
		catch (IOException)
		{
			return LogReadPosition.None;
		}
		catch (UnauthorizedAccessException)
		{
			return LogReadPosition.None;
		}
	}

	private static (LogFileKind Stream, LogEntry Entry)? Newest(StreamScan[] streams)
	{
		StreamScan? winner = null;
		foreach (var stream in streams)
		{
			if (stream.Peek() is not { } candidate)
			{
				continue;
			}

			if (winner?.Peek() is not { } best || candidate.Timestamp > best.Timestamp)
			{
				winner = stream;
			}
		}

		return winner is null ? null : (winner.Stream, winner.Take());
	}

	private sealed class StreamScan
	{
		private readonly IEnumerator<(LogEntry Entry, long BytesRead)> _entries;
		private readonly string? _floorStamp;
		private LogEntry? _current;
		private bool _done;

		public StreamScan(LogFileSet set, LogReadPosition from, string? floorStamp)
		{
			Stream = set.Stream;
			_floorStamp = floorStamp;
			_entries = new ReverseLogEntryReader(set).Read(from).GetEnumerator();
		}

		public LogFileKind Stream { get; }

		public LogEntry? Peek()
		{
			if (_current is not null || _done)
			{
				return _current;
			}

			if (_entries.MoveNext() && !BelowFloor(_entries.Current.Entry))
			{
				_current = _entries.Current.Entry;
			}
			else
			{
				_done = true;
			}

			return _current;
		}

		public LogEntry Take()
		{
			var entry = Peek()!;
			_current = null;

			return entry;
		}

		private bool BelowFloor(LogEntry entry)
			=> _floorStamp is not null &&
				LogEntryId.TryParse(entry.Id, out _, out var position) &&
				string.CompareOrdinal(position.FileStamp, _floorStamp) < 0;
	}
}
