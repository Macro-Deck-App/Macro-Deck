using System.Diagnostics;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Logging;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;

namespace MacroDeckHost.Extensions;

public static class HostBuilderExtensions
{
	public static IHostBuilder ConfigureSerilog(
		this IHostBuilder hostBuilder,
		IMacroDeckPaths paths,
		LogLevelState logLevelState)
	{
		ArgumentNullException.ThrowIfNull(logLevelState);

		var microsoft = logLevelState.RegisterNoiseFloor(LogEventLevel.Information);
		// SQL text at Debug is a redaction surface rather than request diagnostics, so this one stays a
		// hard floor a lowered minimum cannot lift.
		var databaseCommand = logLevelState.RegisterOverride(LogEventLevel.Warning);
		var httpClient = logLevelState.RegisterNoiseFloor(Debugger.IsAttached
			? LogEventLevel.Debug
			: LogEventLevel.Information);
		var requestPipeline = LogNoiseCategories.RequestPipeline
			.Select(category => (Category: category, Switch: logLevelState.RegisterNoiseFloor(LogEventLevel.Warning)))
			.ToList();

		return hostBuilder.UseSerilog((_, _, configuration) =>
		{
			configuration
				.MinimumLevel.ControlledBy(logLevelState.Global)
				.MinimumLevel.Override("Microsoft", microsoft)
				.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", databaseCommand)
				.MinimumLevel.Override("System.Net.Http.HttpClient", httpClient);

			// Serilog picks the most specific override, so these beat the "Microsoft" registration above.
			foreach (var (category, levelSwitch) in requestPipeline)
			{
				configuration.MinimumLevel.Override(category, levelSwitch);
			}

			configuration
				// The origin segment is the only thing that makes the file's lines attributable to a
				// source, integration and category, which is what the log viewer filters by now that
				// it reads these files. It survives redaction because its value is clamped to
				// characters no redaction pattern matches.
				.Enrich.With(new LogOriginEnricher())
				// One redaction pass per event, before it fans out: the console and the file see the
				// same text, and a credential or user name cannot escape through the sink that
				// happens not to use RedactingTextFormatter. A sink added outside this block is not
				// redacted.
				.WriteTo.Redacted(sinks =>
				{
					sinks.Console(theme: AnsiConsoleTheme.Code,
						formatProvider: System.Globalization.CultureInfo.InvariantCulture);
					sinks.File(RedactingTextFormatter.ForFileSink(),
						Path.Combine(paths.LogsDirectory, "host-.log"),
						rollingInterval: RollingInterval.Day,
						retainedFileCountLimit: 14,
						shared: true);
				});
		});
	}
}
