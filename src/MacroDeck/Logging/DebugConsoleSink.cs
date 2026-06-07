using System.IO;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using SuchByte.MacroDeck.GUI.Dialogs;

namespace SuchByte.MacroDeck.Logging;

/// <summary>
/// Serilog sink that forwards rendered log events to the <see cref="DebugConsole"/> window
/// when one is open. It is a no-op while no debug console is shown, so it can stay permanently
/// in the logging pipeline.
/// </summary>
public class DebugConsoleSink : ILogEventSink
{
    private readonly ITextFormatter _formatter;

    public DebugConsoleSink(ITextFormatter formatter)
    {
        _formatter = formatter;
    }

    public void Emit(LogEvent logEvent)
    {
        var console = DebugConsole.Current;
        if (console is null)
        {
            return;
        }

        using var writer = new StringWriter();
        _formatter.Format(logEvent, writer);

        var source = logEvent.Properties.TryGetValue("Source", out var value) &&
            value is ScalarValue { Value: string name }
                ? name
                : "MacroDeck";

        console.AppendText(writer.ToString(), source, ColorForLevel(logEvent.Level));
    }

    private static Color ColorForLevel(LogEventLevel level)
    {
        return level switch
        {
            LogEventLevel.Fatal => Color.FromArgb(255, 99, 99),
            LogEventLevel.Error => Color.FromArgb(255, 99, 99),
            LogEventLevel.Warning => Color.Orange,
            LogEventLevel.Information => Color.White,
            LogEventLevel.Debug => Color.Gray,
            LogEventLevel.Verbose => Color.DarkGray,
            _ => Color.White
        };
    }
}
