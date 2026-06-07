using System.IO;
using Serilog;
using Serilog.Events;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.StartupConfig;

namespace SuchByte.MacroDeck.Logging;

public static class MacroDeckLogger
{
    private static readonly ILogger logger = Log.ForContext(typeof(MacroDeckLogger));

    private static LogLevel _logLevel = LogLevel.Info;

    private static DebugConsole _debugConsole;


    public static void StartDebugConsole()
    {
        if (_debugConsole != null && !_debugConsole.IsDisposed)
        {
            _debugConsole.Dispose();
            _debugConsole.Close();
        }

        _debugConsole = new DebugConsole();
        _debugConsole.Show();
    }

    /// <summary>
    /// Level of what should be logged.
    /// </summary>
    internal static LogLevel LogLevel
    {
        get => _logLevel;
        set
        {
            _logLevel = value;
            logger.Information("Set log level to {LogLevel}", _logLevel);
        }
    }

    internal static bool FileLogging = true;

    // -------------------------------------------------------------------------
    //  Logging API
    //
    //  Use these methods for all new logging. They accept Serilog message
    //  templates with structured properties, an optional exception and an
    //  optional originating plugin, e.g.:
    //
    //      MacroDeckLogger.Error(ex, "Error in {SomeParameter}", parameter);
    //      MacroDeckLogger.Information(plugin, "Connected to {Host}", host);
    //
    //  When no plugin is supplied the event is attributed to Macro Deck itself.
    // -------------------------------------------------------------------------

    private static void Write(
        LogEventLevel level,
        MacroDeckPlugin? plugin,
        Exception? exception,
        string messageTemplate,
        object[] propertyValues)
    {
        var contextLogger = plugin is null
            ? Log.Logger
            : Log.ForContext("Plugin", plugin.Name);
        contextLogger.Write(level, exception, messageTemplate, propertyValues);
    }

    public static void Verbose(string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Verbose, null, null, messageTemplate, propertyValues);

    public static void Verbose(Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Verbose, null, exception, messageTemplate, propertyValues);

    public static void Verbose(MacroDeckPlugin plugin, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Verbose, plugin, null, messageTemplate, propertyValues);

    public static void Verbose(MacroDeckPlugin plugin, Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Verbose, plugin, exception, messageTemplate, propertyValues);

    public static void Debug(string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Debug, null, null, messageTemplate, propertyValues);

    public static void Debug(Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Debug, null, exception, messageTemplate, propertyValues);

    public static void Debug(MacroDeckPlugin plugin, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Debug, plugin, null, messageTemplate, propertyValues);

    public static void Debug(MacroDeckPlugin plugin, Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Debug, plugin, exception, messageTemplate, propertyValues);

    public static void Information(string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Information, null, null, messageTemplate, propertyValues);

    public static void Information(Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Information, null, exception, messageTemplate, propertyValues);

    public static void Information(MacroDeckPlugin plugin, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Information, plugin, null, messageTemplate, propertyValues);

    public static void Information(MacroDeckPlugin plugin, Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Information, plugin, exception, messageTemplate, propertyValues);

    public static void Warning(string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Warning, null, null, messageTemplate, propertyValues);

    public static void Warning(Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Warning, null, exception, messageTemplate, propertyValues);

    public static void Warning(MacroDeckPlugin plugin, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Warning, plugin, null, messageTemplate, propertyValues);

    public static void Warning(MacroDeckPlugin plugin, Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Warning, plugin, exception, messageTemplate, propertyValues);

    public static void Error(string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Error, null, null, messageTemplate, propertyValues);

    public static void Error(Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Error, null, exception, messageTemplate, propertyValues);

    public static void Error(MacroDeckPlugin plugin, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Error, plugin, null, messageTemplate, propertyValues);

    public static void Error(MacroDeckPlugin plugin, Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Error, plugin, exception, messageTemplate, propertyValues);

    public static void Fatal(string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Fatal, null, null, messageTemplate, propertyValues);

    public static void Fatal(Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Fatal, null, exception, messageTemplate, propertyValues);

    public static void Fatal(MacroDeckPlugin plugin, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Fatal, plugin, null, messageTemplate, propertyValues);

    public static void Fatal(MacroDeckPlugin plugin, Exception exception, string messageTemplate, params object[] propertyValues)
        => Write(LogEventLevel.Fatal, plugin, exception, messageTemplate, propertyValues);

    /// <summary>
    /// Debug trace messages for internal debugging
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="classType"></param>
    /// <param name="message"></param>
    [Obsolete("Use MacroDeckLogger.Debug(MacroDeckPlugin, string, params object[]) instead")]
    public static void Trace(MacroDeckPlugin macroDeckPlugin, Type classType, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .Debug("{LogMessage}", message);
    }

    /// <summary>
    /// Log informations that could be useful
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="classType"></param>
    /// <param name="message"></param>
    [Obsolete("Use MacroDeckLogger.Information(MacroDeckPlugin, string, params object[]) instead")]
    public static void Info(MacroDeckPlugin macroDeckPlugin, Type classType, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .Information("{LogMessage}", message);
    }

    /// <summary>
    /// Log warnings if something gone wrong
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="classType"></param>
    /// <param name="message"></param>
    [Obsolete("Use MacroDeckLogger.Warning(MacroDeckPlugin, string, params object[]) instead")]
    public static void Warning(MacroDeckPlugin macroDeckPlugin, Type classType, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .Warning("{LogMessage}", message);
    }

    /// <summary>
    /// Log errors if something gone really wrong
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="classType"></param>
    /// <param name="message"></param>
    [Obsolete("Use MacroDeckLogger.Error(MacroDeckPlugin, string, params object[]) instead")]
    public static void Error(MacroDeckPlugin macroDeckPlugin, Type classType, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .Error("{LogMessage}", message);
    }


    /// <summary>
    /// Debug trace messages for internal debugging
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="message"></param>
    [Obsolete("Use MacroDeckLogger.Debug(MacroDeckPlugin, string, params object[]) instead")]
    public static void Trace(MacroDeckPlugin macroDeckPlugin, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .Debug("{LogMessage}", message);
    }

    /// <summary>
    /// Log informations that could be useful
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="message"></param>
    [Obsolete("Use MacroDeckLogger.Information(MacroDeckPlugin, string, params object[]) instead")]
    public static void Info(MacroDeckPlugin macroDeckPlugin, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .Information("{LogMessage}", message);
    }

    /// <summary>
    /// Log warnings if something gone wrong
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="message"></param>
    [Obsolete("Use MacroDeckLogger.Warning(MacroDeckPlugin, string, params object[]) instead")]
    public static void Warning(MacroDeckPlugin macroDeckPlugin, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .Warning("{LogMessage}", message);
    }

    /// <summary>
    /// Log errors if something gone really wrong
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="message"></param>
    [Obsolete("Use MacroDeckLogger.Error(MacroDeckPlugin, string, params object[]) instead")]
    public static void Error(MacroDeckPlugin macroDeckPlugin, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .Error("{LogMessage}", message);
    }

    internal static void CleanUpLogsDir()
    {
        foreach (var file in new DirectoryInfo(ApplicationPaths.LogsDirectoryPath).GetFiles()
            .Where(p => p.CreationTime < DateTime.Now.AddDays(-30)).ToArray())
        {
            try
            {
                File.Delete(file.FullName);
            }
            catch
            {
            }
        }
    }

    private static string TruncateForDisplay(this string value, int length)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var returnValue = value;
        if (value.Length > length)
        {
            var tmp = value.Substring(0, length);
            if (tmp.LastIndexOf(' ') > 0)
            {
                returnValue = tmp.Substring(0, tmp.LastIndexOf(' ')) + " ...";
            }
        }

        return returnValue;
    }

    internal static string CurrentFilename =>
        Path.Combine(ApplicationPaths.LogsDirectoryPath, DateTime.Now.ToString("yyyy-MM-dd") + ".log");
}

public enum LogLevel
{
    /// <summary>
    /// Log Trace, Info, Warnings and Errors
    /// </summary>
    Trace = 1,

    /// <summary>
    /// Log Info, Warnings and Errors
    /// </summary>
    Info = 2,

    /// <summary>
    /// Log Warnings and Errors
    /// </summary>
    Warning = 3,

    /// <summary>
    /// Log only Errors
    /// </summary>
    Error = 4,

    /// <summary>
    /// Log nothing
    /// </summary>
    Nothing = 100
}
