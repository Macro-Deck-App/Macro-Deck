using System.IO;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog.Sinks.SystemConsole.Themes;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.StartupConfig;

public static class LoggingConfig
{
    private const string OutputTemplate =
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{Source}] {Message:lj}{NewLine}{Exception}";

    /// <summary>
    /// Builds the application-wide Serilog logger. It is assigned to <see cref="Log.Logger"/>
    /// early during startup so logging is live from the beginning; the ASP.NET host reuses the
    /// same static logger through <see cref="ConfigureSerilog"/>.
    /// </summary>
    public static ILogger CreateLogger()
    {
        return new LoggerConfiguration()
            .Enrich.With<PluginSourceEnricher>()
            .MinimumLevel.ControlledBy(MacroDeckLogger.LevelSwitch)
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Information)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
            .WriteTo.Console(theme: AnsiConsoleTheme.Code,
                outputTemplate: OutputTemplate)
            .WriteTo.File(Path.Combine(ApplicationPaths.LogsDirectoryPath, "log.txt"),
                rollingInterval: RollingInterval.Day,
                fileSizeLimitBytes: 53477376, // 50 MB
                outputTemplate: OutputTemplate)
            .WriteTo.Sink(new DebugConsoleSink(new MessageTemplateTextFormatter(OutputTemplate, null)))
            .CreateLogger();
    }

    public static IHostBuilder ConfigureSerilog(this IHostBuilder hostBuilder)
        => hostBuilder.UseSerilog();
}
