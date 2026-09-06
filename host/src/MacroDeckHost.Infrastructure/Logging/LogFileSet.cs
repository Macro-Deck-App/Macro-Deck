using MacroDeckHost.Application.Logging;

namespace MacroDeckHost.Infrastructure.Logging;

public sealed class LogFileSet
{
	private readonly string _directory;

	public LogFileSet(string directory, LogFileKind stream)
	{
		_directory = directory;
		Stream = stream;
	}

	public LogFileKind Stream { get; }

	public string PathFor(string stamp)
		=> Path.Combine(_directory,
			Stream == LogFileKind.Bootstrapper
				? BootstrapperLogParser.FileName(stamp)
				: HostLogParser.FileName(stamp));

	public List<string> Stamps()
	{
		if (!Directory.Exists(_directory))
		{
			return [];
		}

		var pattern = Stream == LogFileKind.Bootstrapper
			? BootstrapperLogParser.SearchPattern
			: HostLogParser.SearchPattern;

		var stamps = new List<string>();
		foreach (var path in Directory.EnumerateFiles(_directory, pattern))
		{
			var name = Path.GetFileName(path);
			var parsed = Stream == LogFileKind.Bootstrapper
				? BootstrapperLogParser.TryParseFileStamp(name, out var stamp)
				: HostLogParser.TryParseFileStamp(name, out stamp);

			if (parsed)
			{
				stamps.Add(stamp);
			}
		}

		stamps.Sort(static (left, right) => string.CompareOrdinal(right, left));

		return stamps;
	}

	public string? NewestStamp()
	{
		var stamps = Stamps();

		return stamps.Count > 0 ? stamps[0] : null;
	}

	public bool TryParseHeader(string line, string stamp, out ParsedLogLine parsed)
	{
		if (Stream != LogFileKind.Bootstrapper)
		{
			return HostLogParser.TryParseHeader(line, out parsed);
		}

		parsed = default;

		return DateOnly.TryParseExact(stamp,
				"yyyyMMdd",
				System.Globalization.CultureInfo.InvariantCulture,
				System.Globalization.DateTimeStyles.None,
				out var date) &&
			BootstrapperLogParser.TryParseHeader(line, date, out parsed);
	}
}
