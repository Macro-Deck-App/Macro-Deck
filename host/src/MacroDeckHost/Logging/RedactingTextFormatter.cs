using System.Globalization;
using MacroDeckHost.Application.Logging;
using Serilog.Events;
using Serilog.Formatting;

namespace MacroDeckHost.Logging;

public sealed class RedactingTextFormatter : ITextFormatter
{
	public const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff zzz";

	public const string ContinuationIndent = " ";

	public static RedactingTextFormatter ForFileSink() => new();

	public void Format(LogEvent logEvent, TextWriter output)
	{
		ArgumentNullException.ThrowIfNull(logEvent);
		ArgumentNullException.ThrowIfNull(output);

		var origin = logEvent.Properties.TryGetValue(LogOriginEnricher.OriginPropertyName, out var value) &&
			value is ScalarValue { Value: string text }
				? text
				: LogOriginEnricher.HostOrigin;

		var line = string.Create(CultureInfo.InvariantCulture,
			$"{logEvent.Timestamp.ToString(TimestampFormat, CultureInfo.InvariantCulture)} " +
			$"[{LevelTag(logEvent.Level)}] [{origin}] {Escape(logEvent.RenderMessage(CultureInfo.InvariantCulture))}");

		output.Write(LogRedactor.Redact(line));
		output.Write(Environment.NewLine);

		if (logEvent.Exception is null)
		{
			return;
		}

		foreach (var exceptionLine in LogRedactor.Redact(logEvent.Exception.ToString()).Split('\n'))
		{
			output.Write(ContinuationIndent);
			output.Write(exceptionLine.TrimEnd('\r'));
			output.Write(Environment.NewLine);
		}
	}

	public static string LevelTag(LogEventLevel level)
		=> level switch
		{
			LogEventLevel.Verbose => "VRB",
			LogEventLevel.Debug => "DBG",
			LogEventLevel.Warning => "WRN",
			LogEventLevel.Error => "ERR",
			LogEventLevel.Fatal => "FTL",
			_ => "INF"
		};

	private static string Escape(string message)
		=> message.Contains('\n', StringComparison.Ordinal) || message.Contains('\r', StringComparison.Ordinal)
			? message.Replace("\r\n", "\\n", StringComparison.Ordinal)
				.Replace("\r", "\\n", StringComparison.Ordinal)
				.Replace("\n", "\\n", StringComparison.Ordinal)
			: message;
}
