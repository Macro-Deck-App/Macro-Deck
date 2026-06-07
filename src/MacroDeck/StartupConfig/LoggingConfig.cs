using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.SystemConsole.Themes;
using SuchByte.MacroDeck.Logging;

namespace SuchByte.MacroDeck.StartupConfig;

public static class LoggingConfig
{
    public static IHostBuilder ConfigureSerilog(this IHostBuilder hostBuilder)
    {
        const string outputTemplate =
            "[{Timestamp:HH:mm:ss} {Level:u3}] [{Source}] {Message:lj}{NewLine}{Exception}";

        return hostBuilder.UseSerilog((_, _, configuration) =>
        {
            configuration
                .Enrich.With<PluginSourceEnricher>()
                .MinimumLevel.Is(!Debugger.IsAttached ? LogEventLevel.Information : LogEventLevel.Debug)
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.AspNetCore.Authentication", LogEventLevel.Information)
                .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("System.Net.Http.HttpClient", LogEventLevel.Warning)
                .WriteTo.Console(theme: AnsiConsoleTheme.Code,
                    outputTemplate: outputTemplate)
                .WriteTo.File(Path.Combine(ApplicationPaths.LogsDirectoryPath, "log.txt"),
                    rollingInterval: RollingInterval.Day,
                    fileSizeLimitBytes: 53477376, // 50 MB
                    outputTemplate: outputTemplate);
        });
    }
}
