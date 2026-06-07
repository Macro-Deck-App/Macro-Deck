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
    /// Builds the application-wide Serilog Logger. It is assigned to <see cref="Log.Logger"/>
    /// early during startup so logging is live from the beginning; the ASP.NET host reuses the
    /// same static Logger.through <see cref="ConfigureSerilog"/>.
    /// </summary>
    public static ILogger CreateLogger()
    {
        var loggerConfiguration = new LoggerConfiguration()
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
            .WriteTo.Sink(new DebugConsoleSink(new MessageTemplateTextFormatter(OutputTemplate, null)));

        // Anonymous error reporting. Only registered when a real DSN was injected in CI/CD; the
        // conditional routes only Macro Deck's own events (not plugins) and honours the opt-out.
        if (SentryConfiguration.IsDsnConfigured)
        {
            loggerConfiguration.WriteTo.Conditional(SentryConfiguration.ShouldSend,
                wt => wt.Sentry(SentryConfiguration.Configure));
        }

        return loggerConfiguration.CreateLogger();
    }

    public static IHostBuilder ConfigureSerilog(this IHostBuilder hostBuilder)
    {
        return hostBuilder.UseSerilog();
    }
}
