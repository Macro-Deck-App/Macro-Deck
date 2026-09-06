using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Infrastructure.Logging;

public sealed class ReverseLogEntryReader
{
	public const int MaxContinuationLines = 500;

	private const int MaxContinuationChars = 64 * 1024;

	private readonly LogFileSet _set;

	public ReverseLogEntryReader(LogFileSet set)
	{
		_set = set;
	}

	public IEnumerable<(LogEntry Entry, long BytesRead)> Read(LogReadPosition from)
	{
		var stamps = _set.Stamps();
		if (stamps.Count == 0)
		{
			yield break;
		}

		var startIndex = 0;
		var startOffset = long.MaxValue;
		if (from.HasFile)
		{
			var index = stamps.IndexOf(from.FileStamp);
			if (index >= 0)
			{
				startIndex = index;
				startOffset = from.Offset;
			}
			else
			{
				startIndex = stamps.Count;
				for (var i = 0; i < stamps.Count; i++)
				{
					if (string.CompareOrdinal(stamps[i], from.FileStamp) < 0)
					{
						startIndex = i;
						break;
					}
				}
			}
		}

		for (var i = startIndex; i < stamps.Count; i++)
		{
			var stamp = stamps[i];
			var path = _set.PathFor(stamp);
			var end = i == startIndex ? startOffset : long.MaxValue;

			foreach (var entry in ReadFile(path, stamp, end))
			{
				yield return entry;
			}
		}
	}

	private IEnumerable<(LogEntry Entry, long BytesRead)> ReadFile(string path, string stamp, long endOffset)
	{
		var lines = SafeLines(path, endOffset);
		var continuations = new List<string>();
		var continuationChars = 0;

		foreach (var line in lines)
		{
			if (!_set.TryParseHeader(line.Text, stamp, out var parsed))
			{
				if (continuations.Count < MaxContinuationLines && continuationChars < MaxContinuationChars)
				{
					var text = Unindent(line.Text);
					continuations.Insert(0, text);
					continuationChars += text.Length;
				}

				continue;
			}

			var entry = new LogEntry
			{
				Id = LogEntryId.Create(_set.Stream, stamp, line.Offset),
				Timestamp = parsed.Timestamp,
				Level = parsed.Level,
				Source = parsed.Source,
				SourceId = parsed.SourceId,
				Category = parsed.Category,
				Message = parsed.Message,
				Exception = continuations.Count == 0 ? null : string.Join('\n', continuations)
			};

			continuations.Clear();
			continuationChars = 0;

			yield return (entry, line.Text.Length + Environment.NewLine.Length);
		}
	}

	public static string Unindent(string line)
		=> line.StartsWith(' ') ? line[1..] : line;

	private static IEnumerable<LogFileLine> SafeLines(string path, long endOffset)
	{
		IEnumerator<LogFileLine> enumerator;
		try
		{
			enumerator = ReverseLineReader.Read(path, endOffset).GetEnumerator();
		}
		catch (IOException)
		{
			yield break;
		}
		catch (UnauthorizedAccessException)
		{
			yield break;
		}

		using (enumerator)
		{
			while (true)
			{
				LogFileLine current;
				try
				{
					if (!enumerator.MoveNext())
					{
						break;
					}

					current = enumerator.Current;
				}
				catch (IOException)
				{
					break;
				}
				catch (UnauthorizedAccessException)
				{
					break;
				}

				yield return current;
			}
		}
	}
}
