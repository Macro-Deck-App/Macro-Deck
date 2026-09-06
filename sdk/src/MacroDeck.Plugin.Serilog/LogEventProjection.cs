using System.Globalization;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Logging;
using Serilog.Events;

namespace MacroDeck.Plugin.Serilog;

/// <summary>
/// Builds the wire DTO from a Serilog <see cref="LogEvent" />, truncating to every protocol cap
/// client-side so the host never has to reject a batch the SDK could have trimmed itself.
/// </summary>
internal static class LogEventProjection
{
	private const string SourceContextPropertyName = "SourceContext";

	public static LogEventDto ToDto(LogEvent logEvent)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		return new LogEventDto
		{
			Timestamp = logEvent.Timestamp,
			Level = MapLevel(logEvent.Level),
			MessageTemplate = Truncate(logEvent.MessageTemplate.Text, ProtocolLimits.MaxLogMessageLength),
			RenderedMessage = Truncate(logEvent.RenderMessage(CultureInfo.InvariantCulture),
				ProtocolLimits.MaxLogMessageLength),
			SourceContext = ExtractSourceContext(logEvent),
			Properties = BuildProperties(logEvent.Properties),
			Exception = logEvent.Exception is { } exception
				? BuildException(exception, ProtocolLimits.MaxLogExceptionDepth)
				: null
		};
	}

	private static string MapLevel(LogEventLevel level)
		=> level switch
		{
			LogEventLevel.Verbose => LogLevels.Verbose,
			LogEventLevel.Debug => LogLevels.Debug,
			LogEventLevel.Information => LogLevels.Information,
			LogEventLevel.Warning => LogLevels.Warning,
			LogEventLevel.Error => LogLevels.Error,
			LogEventLevel.Fatal => LogLevels.Fatal,
			_ => LogLevels.Information
		};

	private static string? ExtractSourceContext(LogEvent logEvent)
	{
		if (!logEvent.Properties.TryGetValue(SourceContextPropertyName, out var value) ||
			value is not ScalarValue { Value: string text })
		{
			return null;
		}

		return Truncate(text, ProtocolLimits.MaxLogSourceContextLength);
	}

	private static Dictionary<string, string>? BuildProperties(
		IReadOnlyDictionary<string, LogEventPropertyValue> properties)
	{
		if (properties.Count == 0)
		{
			return null;
		}

		var result = new Dictionary<string, string>(StringComparer.Ordinal);

		foreach (var (name, value) in properties)
		{
			// Carried on LogEventDto.SourceContext instead - see ExtractSourceContext.
			if (string.Equals(name, SourceContextPropertyName, StringComparison.Ordinal))
			{
				continue;
			}

			if (result.Count >= ProtocolLimits.MaxLogPropertiesPerEvent)
			{
				break;
			}

			var truncatedName = Truncate(name, ProtocolLimits.MaxLogPropertyNameLength);
			if (truncatedName.Length == 0)
			{
				continue;
			}

			result[truncatedName] = Truncate(RenderPropertyValue(value), ProtocolLimits.MaxLogPropertyValueLength);
		}

		return result.Count == 0 ? null : result;
	}

	/// <summary>
	/// Scalars render through <see cref="IFormattable" /> with the invariant culture, so a number or
	/// date does not pick up the process's current culture on its way to the host. A non-scalar
	/// (<c>@Config</c>, an array) degrades to Serilog's own textual rendering of the structure rather
	/// than being dropped - the protocol carries only flat string properties, but "flattened to a
	/// string" and "discarded" are different outcomes.
	/// </summary>
	private static string RenderPropertyValue(LogEventPropertyValue value)
		=> value switch
		{
			ScalarValue { Value: null } => string.Empty,
			ScalarValue { Value: IFormattable formattable } => formattable.ToString(null, CultureInfo.InvariantCulture),
			ScalarValue scalar => scalar.Value?.ToString() ?? string.Empty,
			_ => value.ToString()
		};

	private static LogExceptionDto BuildException(Exception exception, int remainingDepth)
		=> new()
		{
			Type = exception.GetType().FullName ?? exception.GetType().Name,
			Message = exception.Message,
			// Truncated here too, not just on the host's side: an untruncated stack trace is exactly what
			// can push a single-event batch (the one case SendBatchAsync's split cannot shrink any
			// further) over MaxMessageBytes and force it to be dropped rather than shrunk to fit.
			StackTrace = exception.StackTrace is { } stackTrace
				? Truncate(stackTrace, ProtocolLimits.MaxLogExceptionLength)
				: null,
			Inner = exception.InnerException is { } inner && remainingDepth > 1
				? BuildException(inner, remainingDepth - 1)
				: null
		};

	private static string Truncate(string value, int maxLength) =>
		value.Length <= maxLength ? value : value[..maxLength];
}
