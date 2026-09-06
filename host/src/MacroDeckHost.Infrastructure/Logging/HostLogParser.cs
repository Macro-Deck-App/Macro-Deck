using System.Globalization;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Infrastructure.Logging;

public static partial class HostLogParser
{
	private const string FilePrefix = "host-";
	private const string FileSuffix = ".log";

	// The three leading fields are consumed positionally, so a message containing brackets or
	// slashes cannot be mistaken for part of them. The origin group is optional: files written by
	// an older version carry no attribution, and they stay readable for the fortnight they are
	// retained rather than showing up blank.
	[GeneratedRegex(
		@"^(?<timestamp>\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\.\d{3} [+-]\d{2}:\d{2}) \[(?<level>[A-Z]{3})\] (?:\[(?<origin>Host|Integration/[A-Za-z0-9._-]+)(?:/(?<category>[A-Za-z0-9._-]+))?\] )?(?<message>.*)$",
		RegexOptions.CultureInvariant)]
	private static partial Regex HeaderPattern();

	public static bool TryParseFileStamp(string fileName, out string stamp)
	{
		stamp = string.Empty;
		if (string.IsNullOrEmpty(fileName) ||
			!fileName.StartsWith(FilePrefix, StringComparison.Ordinal) ||
			!fileName.EndsWith(FileSuffix, StringComparison.Ordinal))
		{
			return false;
		}

		var candidate = fileName[FilePrefix.Length..^FileSuffix.Length];
		if (!DateOnly.TryParseExact(candidate,
			"yyyyMMdd",
			CultureInfo.InvariantCulture,
			DateTimeStyles.None,
			out _))
		{
			return false;
		}

		stamp = candidate;

		return true;
	}

	public static string FileName(string stamp) => $"{FilePrefix}{stamp}{FileSuffix}";

	public static string SearchPattern => $"{FilePrefix}*{FileSuffix}";

	public static bool TryParseHeader(string line, out ParsedLogLine parsed)
	{
		parsed = default;
		if (string.IsNullOrEmpty(line))
		{
			return false;
		}

		var match = HeaderPattern().Match(line);
		if (!match.Success ||
			!DateTimeOffset.TryParseExact(match.Groups["timestamp"].Value,
				"yyyy-MM-dd HH:mm:ss.fff zzz",
				CultureInfo.InvariantCulture,
				DateTimeStyles.None,
				out var timestamp))
		{
			return false;
		}

		var origin = match.Groups["origin"];
		var integrationId = origin.Success && origin.Value.StartsWith("Integration/", StringComparison.Ordinal)
			? origin.Value["Integration/".Length..]
			: null;

		parsed = new ParsedLogLine
		{
			Timestamp = timestamp,
			Level = BootstrapperLogParser.ParseLevel(match.Groups["level"].Value),
			Source = integrationId is null ? LogEntrySource.Host : LogEntrySource.Integration,
			SourceId = integrationId,
			Category = match.Groups["category"].Success ? match.Groups["category"].Value : null,
			Message = match.Groups["message"].Value
		};

		return true;
	}
}

public readonly record struct ParsedLogLine
{
	public required DateTimeOffset Timestamp { get; init; }

	public required LogEntryLevel Level { get; init; }

	public required LogEntrySource Source { get; init; }

	public string? SourceId { get; init; }

	public string? Category { get; init; }

	public required string Message { get; init; }
}
