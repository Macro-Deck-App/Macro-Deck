using System.Globalization;
using System.Text.RegularExpressions;
using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Infrastructure.Logging;

public static partial class BootstrapperLogParser
{
	private const string FilePrefix = "bootstrapper-";
	private const string FileSuffix = ".log";

	[GeneratedRegex(@"^\[(?<time>\d{2}:\d{2}:\d{2}) (?<level>[A-Z]{3})\] (?<message>.*)$",
		RegexOptions.CultureInvariant)]
	private static partial Regex LinePattern();

	public static bool TryParseFileDate(string fileName, out DateOnly date)
	{
		date = default;
		if (string.IsNullOrEmpty(fileName) ||
			!fileName.StartsWith(FilePrefix, StringComparison.Ordinal) ||
			!fileName.EndsWith(FileSuffix, StringComparison.Ordinal))
		{
			return false;
		}

		var stamp = fileName[FilePrefix.Length..^FileSuffix.Length];

		return DateOnly.TryParseExact(stamp, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
	}

	public static bool TryParseHeader(string line, DateOnly date, out ParsedLogLine parsed)
	{
		parsed = default;
		if (string.IsNullOrEmpty(line))
		{
			return false;
		}

		var match = LinePattern().Match(line);
		if (!match.Success)
		{
			return false;
		}

		if (!TimeOnly.TryParseExact(match.Groups["time"].Value,
			"HH:mm:ss",
			CultureInfo.InvariantCulture,
			DateTimeStyles.None,
			out var time))
		{
			return false;
		}

		var timestamp = new DateTime(date, time, DateTimeKind.Unspecified);

		parsed = new ParsedLogLine
		{
			Timestamp = new DateTimeOffset(timestamp, TimeZoneInfo.Local.GetUtcOffset(timestamp)),
			Level = ParseLevel(match.Groups["level"].Value),
			Source = LogEntrySource.Bootstrapper,
			Message = match.Groups["message"].Value
		};

		return true;
	}

	public static string FileName(string stamp) => $"{FilePrefix}{stamp}{FileSuffix}";

	public static string SearchPattern => $"{FilePrefix}*{FileSuffix}";

	public static bool TryParseFileStamp(string fileName, out string stamp)
	{
		stamp = TryParseFileDate(fileName, out var date)
			? date.ToString("yyyyMMdd", CultureInfo.InvariantCulture)
			: string.Empty;

		return stamp.Length > 0;
	}

	public static LogEntryLevel ParseLevel(string tag)
		=> tag switch
		{
			"VRB" => LogEntryLevel.Verbose,
			"DBG" => LogEntryLevel.Debug,
			"WRN" => LogEntryLevel.Warning,
			"ERR" => LogEntryLevel.Error,
			"FTL" => LogEntryLevel.Fatal,
			_ => LogEntryLevel.Information
		};
}
