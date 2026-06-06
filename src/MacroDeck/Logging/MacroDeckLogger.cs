using System.Diagnostics;
using System.IO;
using Serilog;
using SuchByte.MacroDeck.GUI.CustomControls;
using SuchByte.MacroDeck.GUI.Dialogs;
using SuchByte.MacroDeck.Notifications;
using SuchByte.MacroDeck.Plugins;
using SuchByte.MacroDeck.Properties;
using SuchByte.MacroDeck.StartupConfig;
using SuchByte.MacroDeck.Utils;

namespace SuchByte.MacroDeck.Logging;

public static class MacroDeckLogger
{
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
            Info("Set log level to " + _logLevel);
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

    internal static void Trace(Type classType, string message)
    {
        Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, classType.FullName)
            .Debug("{LogMessage}", message);
    }

    internal static void Info(Type classType, string message)
    {
        Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, classType.FullName)
            .Information("{LogMessage}", message);
    }

    internal static void Warning(Type classType, string message)
    {
        Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, classType.FullName)
            .Warning("{LogMessage}", message);
    }

    internal static void Error(Type classType, string message)
    {
        Log.ForContext(Serilog.Core.Constants.SourceContextPropertyName, classType.FullName)
            .Error("{LogMessage}", message);
    }


    internal static void Trace(string message)
    {
        Log.Debug("{LogMessage}", message);
    }

    internal static void Info(string message)
    {
        Log.Information("{LogMessage}", message);
    }

    internal static void Warning(string message)
    {
        Log.Warning("{LogMessage}", message);
    }

    internal static void Error(string message)
    {
        Log.Error("{LogMessage}", message);
    }

    private static void OpenLatestLog()
    {
        var p = new Process
        {
            StartInfo = new ProcessStartInfo(CurrentFilename)
            {
                UseShellExecute = true
            }
        };
        p.Start();
    }

    private static void LogInternal(string sender, LogLevel logLevel, string message)
    {
        if (logLevel == LogLevel.Error)
        {
            var btnShowLog = new ButtonPrimary
            {
                Size = new Size(75, 25),
                AutoSize = true,
                Text = "Show log"
            };
            btnShowLog.Click += (sender, e) => { OpenLatestLog(); };

            NotificationManager.SystemNotification("Error",
                $"{sender} caused an error: {TruncateForDisplay(message, 250)}",
                controls: new List<Control> { btnShowLog },
                icon: Resources.Macro_Deck_error);
        }

        if ((!Debugger.IsAttached && !FileLogging) || logLevel < LogLevel)
        {
            return;
        }

        var formattedLog = string.Format("{0} [{1}] [{2}] >> {3}",
            DateTime.Now.ToString("T"),
            sender,
            logLevel.ToString(),
            message);
        if (Debugger.IsAttached)
        {
            Debug.WriteLine(formattedLog);
        }

        if (_debugConsole != null && !_debugConsole.IsDisposed && _debugConsole.Visible)
        {
            var logColor = Color.White;
            switch (logLevel)
            {
                case LogLevel.Info:
                    logColor = Color.Aqua;
                    break;
                case LogLevel.Warning:
                    logColor = Color.Orange;
                    break;
                case LogLevel.Error:
                    logColor = Color.Red;
                    break;
            }

            _debugConsole.AppendText(formattedLog + Environment.NewLine, sender, logColor);
        }

        if (FileLogging && Directory.Exists(ApplicationPaths.LogsDirectoryPath) && CurrentFilename != null)
        {
            try
            {
                Retry.Do(() =>
                    {
                        File.AppendAllText(CurrentFilename, Environment.NewLine + formattedLog, Encoding.UTF8);
                    },
                    TimeSpan.FromMilliseconds(10));
            }
            catch (Exception ex)
            {
                FileLogging = false;
                LogInternal(sender, LogLevel.Error, "File logging failed: " + ex.Message);
                FileLogging = true;
            }
        }
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
