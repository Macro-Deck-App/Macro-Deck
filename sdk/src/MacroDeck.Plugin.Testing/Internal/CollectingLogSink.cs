using System.Globalization;
using MacroDeck.Plugin.Protocol.Logging;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeck.Plugin.Testing.Internal;

/// <summary>
/// Captures every Serilog event a plugin under test writes directly into a
/// <see cref="PluginLogCollector" />, for <see cref="PluginTestHarness" />, which has no wire for a
/// plugin's own <c>log.publish</c> forwarding to travel over.
///
/// <para>
/// Timestamps come from the harness clock rather than from the event, so an assertion about when
/// something was logged reads the same manual time everything else in a harness test does.
/// </para>
/// </summary>
internal sealed class CollectingLogSink(PluginLogCollector logs, TimeProvider clock) : ILogEventSink
{
	private const string SourceContextPropertyName = "SourceContext";

	public void Emit(LogEvent logEvent)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		logs.Record(new CollectedLogEvent
		{
			Timestamp = clock.GetUtcNow(),
			Level = MapLevel(logEvent.Level),
			SourceContext = SourceContextOf(logEvent),
			MessageTemplate = logEvent.MessageTemplate.Text,
			Message = logEvent.RenderMessage(CultureInfo.InvariantCulture),
			Properties = BuildProperties(logEvent),
			Exception = logEvent.Exception is null ? null : BuildException(logEvent.Exception)
		});
	}

	private static string MapLevel(LogEventLevel level) => level switch
	{
		LogEventLevel.Verbose => LogLevels.Verbose,
		LogEventLevel.Debug => LogLevels.Debug,
		LogEventLevel.Information => LogLevels.Information,
		LogEventLevel.Warning => LogLevels.Warning,
		LogEventLevel.Error => LogLevels.Error,
		LogEventLevel.Fatal => LogLevels.Fatal,
		_ => LogLevels.Information
	};

	private static string? SourceContextOf(LogEvent logEvent)
		=> logEvent.Properties.TryGetValue(SourceContextPropertyName, out var value) &&
			value is ScalarValue { Value: string text }
				? text
				: null;

	private static Dictionary<string, string> BuildProperties(LogEvent logEvent)
	{
		var properties = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var (name, value) in logEvent.Properties)
		{
			// Carried on CollectedLogEvent.SourceContext instead - see SourceContextOf.
			if (string.Equals(name, SourceContextPropertyName, StringComparison.Ordinal))
			{
				continue;
			}

			properties[name] = Render(value);
		}

		return properties;
	}

	private static string Render(LogEventPropertyValue value)
		=> value switch
		{
			ScalarValue { Value: null } => string.Empty,
			ScalarValue { Value: IFormattable formattable } => formattable.ToString(null, CultureInfo.InvariantCulture),
			ScalarValue scalar => scalar.Value?.ToString() ?? string.Empty,
			_ => value.ToString()
		};

	private static LogExceptionDto BuildException(Exception exception)
		=> new()
		{
			Type = exception.GetType().FullName ?? exception.GetType().Name,
			Message = exception.Message,
			StackTrace = exception.StackTrace,
			Inner = exception.InnerException is null ? null : BuildException(exception.InnerException)
		};
}
