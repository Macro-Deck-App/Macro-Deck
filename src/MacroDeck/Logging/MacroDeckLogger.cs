using System.IO;
using Serilog;
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

    /// <summary>
    /// Debug trace messages for internal debugging
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="classType"></param>
    /// <param name="message"></param>
    [Obsolete("Use Serilog instead")]
    public static void Trace(MacroDeckPlugin macroDeckPlugin, Type classType, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .ForContext(Serilog.Core.Constants.SourceContextPropertyName, classType.FullName)
            .Debug("{LogMessage}", message);
    }

    /// <summary>
    /// Log informations that could be useful
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="classType"></param>
    /// <param name="message"></param>
    [Obsolete("Use Serilog instead")]
    public static void Info(MacroDeckPlugin macroDeckPlugin, Type classType, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .ForContext(Serilog.Core.Constants.SourceContextPropertyName, classType.FullName)
            .Information("{LogMessage}", message);
    }

    /// <summary>
    /// Log warnings if something gone wrong
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="classType"></param>
    /// <param name="message"></param>
    [Obsolete("Use Serilog instead")]
    public static void Warning(MacroDeckPlugin macroDeckPlugin, Type classType, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .ForContext(Serilog.Core.Constants.SourceContextPropertyName, classType.FullName)
            .Warning("{LogMessage}", message);
    }

    /// <summary>
    /// Log errors if something gone really wrong
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="classType"></param>
    /// <param name="message"></param>
    [Obsolete("Use Serilog instead")]
    public static void Error(MacroDeckPlugin macroDeckPlugin, Type classType, string message)
    {
        Log.ForContext("Plugin", macroDeckPlugin.Name)
            .ForContext(Serilog.Core.Constants.SourceContextPropertyName, classType.FullName)
            .Error("{LogMessage}", message);
    }


    /// <summary>
    /// Debug trace messages for internal debugging
    /// </summary>
    /// <param name="macroDeckPlugin"></param>
    /// <param name="message"></param>
    [Obsolete("Use Serilog instead")]
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
    [Obsolete("Use Serilog instead")]
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
    [Obsolete("Use Serilog instead")]
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
    [Obsolete("Use Serilog instead")]
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
