using System.Text;
using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Infrastructure.Logging;

public sealed class LogFileTailReader
{
	public const int MaxReadBytes = 512 * 1024;

	public const int MaxBacklogBytes = 4 * 1024 * 1024;

	private readonly LogFileSet _set;

	private (string FileStamp, long Offset)? _held;

	public LogFileTailReader(LogFileSet set)
	{
		_set = set;
	}

	public (IReadOnlyList<LogEntry> Entries, LogReadPosition Position) Read(LogReadPosition start)
	{
		var newest = _set.NewestStamp();
		if (newest is null)
		{
			return ([], start);
		}

		var position = start.HasFile && _set.Stamps().Contains(start.FileStamp)
			? start
			: new LogReadPosition(newest, 0);

		var entries = new List<LogEntry>();

		if (!string.Equals(position.FileStamp, newest, StringComparison.Ordinal))
		{
			var (drained, drainedPosition) = ReadFile(position);
			entries.AddRange(drained);

			if (drainedPosition.Offset < EndOfFile(_set.PathFor(position.FileStamp)))
			{
				return (entries, drainedPosition);
			}

			position = new LogReadPosition(newest, 0);
		}

		var (fresh, next) = ReadFile(position);
		entries.AddRange(fresh);

		return (entries, next);
	}

	private (IReadOnlyList<LogEntry> Entries, LogReadPosition Position) ReadFile(LogReadPosition start)
	{
		var path = _set.PathFor(start.FileStamp);
		var offset = start.Offset;
		string text;

		try
		{
			using var stream = ReverseLineReader.OpenShared(path);

			if (stream.Length < offset)
			{
				offset = 0;
			}

			if (stream.Length - offset > MaxBacklogBytes)
			{
				offset = stream.Length - MaxReadBytes;
			}

			var available = (int)Math.Min(MaxReadBytes, stream.Length - offset);
			if (available <= 0)
			{
				return ([], start with { Offset = offset });
			}

			stream.Seek(offset, SeekOrigin.Begin);
			var buffer = new byte[available];
			var read = stream.Read(buffer, 0, available);
			if (read == 0)
			{
				return ([], start with { Offset = offset });
			}

			text = Encoding.UTF8.GetString(buffer, 0, read);
			start = start with { Offset = offset };
			offset += read;
		}
		catch (IOException)
		{
			return ([], start);
		}
		catch (UnauthorizedAccessException)
		{
			return ([], start);
		}

		return Parse(text, start);
	}

	private (IReadOnlyList<LogEntry> Entries, LogReadPosition Position) Parse(string text, LogReadPosition start)
	{
		var entries = new List<LogEntry>();
		var lineStart = start.Offset;
		var consumed = start.Offset;

		ParsedLogLine? pending = null;
		var pendingOffset = 0L;
		var continuations = new List<string>();

		var lines = text.Split('\n');
		for (var i = 0; i < lines.Length; i++)
		{
			var raw = lines[i];

			if (i == lines.Length - 1)
			{
				break;
			}

			var line = raw.TrimEnd('\r');
			var offset = lineStart;
			lineStart += Encoding.UTF8.GetByteCount(raw) + 1;

			if (_set.TryParseHeader(line, start.FileStamp, out var parsed))
			{
				if (pending is { } previous)
				{
					entries.Add(Build(previous, start.FileStamp, pendingOffset, continuations));
					consumed = offset;
				}

				pending = parsed;
				pendingOffset = offset;
				continuations = [];

				continue;
			}

			if (pending is not null && continuations.Count < ReverseLogEntryReader.MaxContinuationLines)
			{
				continuations.Add(ReverseLogEntryReader.Unindent(line));
			}
			else if (pending is null)
			{
				consumed = lineStart;
			}
		}

		if (pending is null)
		{
			_held = null;
			consumed = lineStart;
		}
		else if (_held == (start.FileStamp, pendingOffset))
		{
			entries.Add(Build(pending.Value, start.FileStamp, pendingOffset, continuations));
			_held = null;
			consumed = lineStart;
		}
		else
		{
			// Hold the newest entry back one read: its stack trace may still be being written, and
			// pushing a bare header would show an error with its detail missing.
			_held = (start.FileStamp, pendingOffset);
			consumed = pendingOffset;
		}

		return (entries, start with { Offset = consumed });
	}

	private LogEntry Build(ParsedLogLine parsed, string stamp, long offset, List<string> continuations)
		=> new()
		{
			Id = LogEntryId.Create(_set.Stream, stamp, offset),
			Timestamp = parsed.Timestamp,
			Level = parsed.Level,
			Source = parsed.Source,
			SourceId = parsed.SourceId,
			Category = parsed.Category,
			Message = parsed.Message,
			Exception = continuations.Count == 0 ? null : string.Join('\n', continuations)
		};

	private static long EndOfFile(string path)
	{
		try
		{
			return ReverseLineReader.EndOfLastCompleteLine(path);
		}
		catch (IOException)
		{
			return 0;
		}
		catch (UnauthorizedAccessException)
		{
			return 0;
		}
	}
}
