using System.Globalization;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Infrastructure.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace MacroDeckHost.Logging;

public sealed class HostLogPipeline
{
	public const long FileSizeLimitBytes = 32 * 1024 * 1024;
	public const int RetainedFileCount = 32;

	public static readonly TimeSpan RetainedFileAge = TimeSpan.FromDays(14);

	private const long OversizeAllowanceBytes = 1024 * 1024;

	private readonly string _logsDirectory;
	private readonly string _filePath;
	private readonly long _fileSizeLimitBytes;
	private readonly int _retainedFileCount;
	private readonly LoggingLevelSwitch _global;
	private readonly List<(string Category, LoggingLevelSwitch Switch)> _overrides;

	public HostLogPipeline(IMacroDeckPaths paths, LogLevelState logLevelState)
		: this(paths, logLevelState, FileSizeLimitBytes, RetainedFileCount)
	{
	}

	internal HostLogPipeline(
		IMacroDeckPaths paths,
		LogLevelState logLevelState,
		long fileSizeLimitBytes,
		int retainedFileCount)
	{
		ArgumentNullException.ThrowIfNull(paths);
		ArgumentNullException.ThrowIfNull(logLevelState);

		_logsDirectory = paths.LogsDirectory;
		_filePath = Path.Combine(paths.LogsDirectory, "host-.log");
		_fileSizeLimitBytes = fileSizeLimitBytes;
		_retainedFileCount = retainedFileCount;
		_global = logLevelState.Global;
		_overrides =
		[
			("Microsoft", logLevelState.RegisterNoiseFloor(LogEventLevel.Information)),
			// SQL text at Debug is a redaction surface rather than request diagnostics, so this one stays a
			// hard floor a lowered minimum cannot lift.
			("Microsoft.EntityFrameworkCore.Database.Command", logLevelState.RegisterOverride(LogEventLevel.Warning)),
			("System.Net.Http.HttpClient", logLevelState.RegisterNoiseFloor(LogEventLevel.Warning)),
			// Serilog picks the most specific override, so these beat the Microsoft registration above.
			.. LogNoiseCategories.RequestPipeline
				.Select(category => (category, logLevelState.RegisterNoiseFloor(LogEventLevel.Warning)))
		];
	}

	// Files written before the size limit existed reach 1 GB each and would otherwise stay for their
	// 14 days. The allowance covers the one event the sink writes past the limit before it rolls.
	public long RemoveOversizedFiles()
	{
		List<FileInfo> oversized;
		try
		{
			if (!Directory.Exists(_logsDirectory))
			{
				return 0;
			}

			oversized =
			[
				.. new DirectoryInfo(_logsDirectory)
					.EnumerateFiles(HostLogParser.SearchPattern)
					.Where(file => HostLogParser.TryParseFileStamp(file.Name, out _)
						&& file.Length > _fileSizeLimitBytes + OversizeAllowanceBytes)
			];
		}
		catch (Exception e) when (e is IOException or UnauthorizedAccessException)
		{
			return 0;
		}

		long removedBytes = 0;
		foreach (var file in oversized)
		{
			try
			{
				var length = file.Length;
				file.Delete();
				removedBytes += length;
			}
			catch (Exception e) when (e is IOException or UnauthorizedAccessException)
			{
			}
		}

		return removedBytes;
	}

	// Every logger that writes the host file is configured here: a category bound to the bootstrap
	// logger, as the static EF Core logger factory is, must obey the same floors as the host logger.
	public LoggerConfiguration Apply(LoggerConfiguration configuration, ConsoleTheme? consoleTheme)
	{
		ArgumentNullException.ThrowIfNull(configuration);

		configuration.MinimumLevel.ControlledBy(_global);

		foreach (var (category, levelSwitch) in _overrides)
		{
			configuration.MinimumLevel.Override(category, levelSwitch);
		}

		return configuration
			// The origin segment is what the log viewer filters by. It survives redaction because its
			// value is clamped to characters no redaction pattern matches.
			.Enrich.With(new LogOriginEnricher())
			// One redaction pass per event, before it fans out to the console and the file. A sink
			// added outside this block is not redacted.
			.WriteTo.Redacted(sinks =>
			{
				sinks.Console(theme: consoleTheme, formatProvider: CultureInfo.InvariantCulture);
				sinks.File(RedactingTextFormatter.ForFileSink(),
					_filePath,
					fileSizeLimitBytes: _fileSizeLimitBytes,
					rollingInterval: RollingInterval.Day,
					rollOnFileSizeLimit: true,
					retainedFileCountLimit: _retainedFileCount,
					retainedFileTimeLimit: RetainedFileAge,
					shared: true);
			});
	}
}
