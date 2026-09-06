using System.Text;
using MacroDeck.Plugin.Protocol.Logging;
using Serilog.Debugging;

namespace MacroDeck.Plugin.Serilog;

/// <summary>
/// A bounded local diagnostic tail, written to only while the host is unreachable. Never read back by
/// this SDK and never sent anywhere - see this package's README for why it is not a replay buffer:
/// ADR 0026 and <c>log.publish</c>'s fire-and-forget contract already fix at-most-once delivery with no
/// replay log, and this file existing does not change that.
///
/// <para>
/// Bounded by wrapping, not by freezing once full: once <see cref="_maxBytes" /> would be exceeded the
/// file is truncated and writing continues, so a long outage still ends with the most recent entries on
/// disk - the ones most likely to explain why the connection is still down - rather than with whatever
/// happened to be logged first when the outage began.
/// </para>
/// </summary>
internal sealed class MacroDeckLogFallbackFile
{
	private const string TruncationMarker = "--- fallback log truncated (older entries dropped) ---";

	private readonly string _path;
	private readonly int _maxBytes;
	private readonly Lock _gate = new();

	public MacroDeckLogFallbackFile(string path, int maxBytes)
	{
		ArgumentNullException.ThrowIfNull(path);
		_path = path;
		_maxBytes = Math.Max(1, maxBytes);
	}

	/// <summary>Tees a batch that failed to send to the file, appending line by line and wrapping around
	/// (see this type's remarks) whenever the cap would otherwise be exceeded.</summary>
	public void Append(IReadOnlyList<LogEventDto> events)
	{
		if (events.Count == 0)
		{
			return;
		}

		lock (_gate)
		{
			try
			{
				EnsureDirectory();

				foreach (var line in events.Select(FormatLine))
				{
					AppendLine(line);
				}
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				SelfLog.WriteLine("MacroDeckLogShipper: could not write to the fallback log file: {0}", exception);
			}
		}
	}

	/// <summary>Clears the file. Called once the shipper has a working connection again, so a stale
	/// diagnostic tail from a past outage does not linger next to a fresh one.</summary>
	public void Reset()
	{
		lock (_gate)
		{
			try
			{
				EnsureDirectory();
				File.WriteAllText(_path, string.Empty);
			}
			catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
			{
				SelfLog.WriteLine("MacroDeckLogShipper: could not reset the fallback log file: {0}", exception);
			}
		}
	}

	private void AppendLine(string line)
	{
		var text = line + Environment.NewLine;
		var currentLength = File.Exists(_path) ? new FileInfo(_path).Length : 0;

		if (currentLength + Encoding.UTF8.GetByteCount(text) > _maxBytes)
		{
			File.WriteAllText(_path, TruncationMarker + Environment.NewLine);
		}

		File.AppendAllText(_path, text);
	}

	private void EnsureDirectory()
	{
		var directory = Path.GetDirectoryName(_path);
		if (!string.IsNullOrEmpty(directory))
		{
			Directory.CreateDirectory(directory);
		}
	}

	private static string FormatLine(LogEventDto dto)
		=> $"{dto.Timestamp:O} [{dto.Level}] {dto.RenderedMessage}";
}
