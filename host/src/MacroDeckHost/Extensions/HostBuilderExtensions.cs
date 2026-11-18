using MacroDeckHost.Logging;
using Serilog;
using Serilog.Sinks.SystemConsole.Themes;

namespace MacroDeckHost.Extensions;

public static class HostBuilderExtensions
{
	public static IHostBuilder ConfigureSerilog(this IHostBuilder hostBuilder, HostLogPipeline pipeline)
	{
		ArgumentNullException.ThrowIfNull(pipeline);

		// Use only the central, filtered and redacted pipeline, including on Windows where
		// CreateDefaultBuilder registers an EventLog provider.
		hostBuilder.ConfigureLogging(logging => logging.ClearProviders());

		return hostBuilder.UseSerilog((_, _, configuration) => pipeline.Apply(configuration, AnsiConsoleTheme.Code));
	}
}
