using System.Diagnostics;
using Sentry.Serilog;
using Serilog.Events;

namespace SuchByte.MacroDeck.Logging;

public static class SentryConfiguration
{
    /// <summary>Placeholder replaced with the real DSN in CI/CD. Do not commit a real DSN.</summary>
    private const string Dsn = "__SENTRY_DSN__";

    private const string SourceNamespace = "SuchByte.MacroDeck";

    private static readonly string UserProfilePath =
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    private static readonly string UserName = Environment.UserName;

    public static bool Enabled = true;

    public static bool IsDsnConfigured => Dsn.StartsWith("http", StringComparison.OrdinalIgnoreCase);

    public static void Configure(SentrySerilogOptions options)
    {
        options.Dsn = Dsn;
        options.MinimumEventLevel = LogEventLevel.Error;
        options.MinimumBreadcrumbLevel = LogEventLevel.Warning;
        options.SendDefaultPii = false;
        options.ServerName = null;
        options.AttachStacktrace = true;
        options.Release
            = $"macro-deck@{typeof(SentryConfiguration).Assembly.GetName().Version?.ToString() ?? "unknown"}";
        options.Environment = Debugger.IsAttached ? "debug" : "release";
        options.SetBeforeSend(BeforeSend);
        options.SetBeforeBreadcrumb(BeforeBreadcrumb);
    }
    
    public static bool ShouldSend(LogEvent logEvent)
    {
        return Enabled && IsMacroDeckEvent(logEvent);
    }

    private static bool IsMacroDeckEvent(LogEvent logEvent)
    {
        // Explicitly plugin-attributed (set by MacroDeckLogger plugin overloads).
        if (logEvent.Properties.ContainsKey("Plugin"))
        {
            return false;
        }

        // Strict whitelist by source namespace; anything without a SuchByte.MacroDeck SourceContext
        // (plugins using raw Serilog, framework logs, ...) is excluded.
        return logEvent.Properties.TryGetValue(Serilog.Core.Constants.SourceContextPropertyName, out var value) &&
            value is ScalarValue { Value: string sourceContext } &&
            sourceContext.StartsWith(SourceNamespace, StringComparison.Ordinal);
    }

    private static SentryEvent? BeforeSend(SentryEvent sentryEvent, SentryHint hint)
    {
        if (!Enabled)
        {
            return null;
        }

        sentryEvent.ServerName = null;

        if (sentryEvent.Message is { } message)
        {
            sentryEvent.Message = new SentryMessage
            {
                Message = Scrub(message.Message),
                Formatted = Scrub(message.Formatted),
                Params = message.Params
            };
        }

        if (sentryEvent.SentryExceptions is { } exceptions)
        {
            foreach (var exception in exceptions)
            {
                exception.Value = Scrub(exception.Value);
                if (exception.Stacktrace?.Frames is { } frames)
                {
                    foreach (var frame in frames)
                    {
                        frame.FileName = Scrub(frame.FileName);
                        frame.AbsolutePath = Scrub(frame.AbsolutePath);
                    }
                }
            }
        }

        return sentryEvent;
    }

    private static Breadcrumb? BeforeBreadcrumb(Breadcrumb breadcrumb, SentryHint hint)
    {
        if (!Enabled)
        {
            return null;
        }

        IReadOnlyDictionary<string, string>? data = null;
        if (breadcrumb.Data is { } original)
        {
            data = original.ToDictionary(pair => pair.Key, pair => Scrub(pair.Value) ?? string.Empty);
        }

        // Breadcrumb is immutable; reconstruct it with scrubbed values.
        return new Breadcrumb(Scrub(breadcrumb.Message),
            breadcrumb.Type,
            data,
            breadcrumb.Category,
            breadcrumb.Level);
    }

    /// <summary>Removes the Windows user profile path and account name from a string.</summary>
    private static string? Scrub(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (!string.IsNullOrEmpty(UserProfilePath))
        {
            value = value.Replace(UserProfilePath, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrEmpty(UserName))
        {
            value = value.Replace(UserName, "[user]", StringComparison.OrdinalIgnoreCase);
        }

        return value;
    }
}
