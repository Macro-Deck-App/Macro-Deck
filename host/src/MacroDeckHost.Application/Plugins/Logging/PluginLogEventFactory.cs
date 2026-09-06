using System.Globalization;
using System.Text;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeck.Sdk.Logging;
using Serilog.Events;
using Serilog.Parsing;

namespace MacroDeckHost.Application.Plugins.Logging;

public static class PluginLogEventFactory
{
	public const string MessageTemplatePropertyName = "MacroDeckLogMessageTemplate";

	public const string SessionIdPropertyName = "MacroDeckPluginSessionId";

	public const string PluginVersionPropertyName = "MacroDeckPluginVersion";

	public const string ProcessIdPropertyName = "MacroDeckPluginProcessId";

	public const string AssertedTimestampPropertyName = "MacroDeckLogAssertedTimestamp";

	private const string LogOriginPropertyName = "LogOrigin";

	private const string SourceContextPropertyName = "SourceContext";

	private static readonly TimeSpan _timestampWindow = TimeSpan.FromMinutes(5);

	private static readonly HashSet<string> _reservedPropertyNames = new(StringComparer.Ordinal)
	{
		IntegrationLog.IntegrationPropertyName,
		LogOriginPropertyName,
		SourceContextPropertyName,
		SessionIdPropertyName,
		PluginVersionPropertyName,
		ProcessIdPropertyName,
		MessageTemplatePropertyName,
	};

	public static LogEvent Create(
		string pluginId,
		string sessionId,
		string? declaredVersion,
		int? processId,
		LogEventDto dto,
		TimeProvider timeProvider)
	{
		ArgumentNullException.ThrowIfNull(pluginId);
		ArgumentNullException.ThrowIfNull(sessionId);
		ArgumentNullException.ThrowIfNull(dto);
		ArgumentNullException.ThrowIfNull(timeProvider);

		var level = MapLevel(dto.Level);
		var renderedMessage = Truncate(dto.RenderedMessage, ProtocolLimits.MaxLogMessageLength);
		var messageTemplate = new MessageTemplate([new TextToken(renderedMessage)]);

		var now = timeProvider.GetUtcNow();
		var timestamp = dto.Timestamp;
		string? assertedTimestamp = null;
		if (timestamp < now - _timestampWindow || timestamp > now + _timestampWindow)
		{
			assertedTimestamp = timestamp.ToString("O", CultureInfo.InvariantCulture);
			timestamp = now;
		}

		var exception = dto.Exception is { } exceptionDto
			? ForwardedPluginException.FromComposedText(ComposeException(exceptionDto))
			: null;

		var properties = BuildProperties(dto.Properties);
		properties.Add(new LogEventProperty(MessageTemplatePropertyName,
			new ScalarValue(Truncate(dto.MessageTemplate, ProtocolLimits.MaxLogMessageLength))));

		var logEvent = new LogEvent(timestamp, level, exception, messageTemplate, properties);

		if (assertedTimestamp is not null)
		{
			logEvent.AddOrUpdateProperty(new LogEventProperty(AssertedTimestampPropertyName,
				new ScalarValue(assertedTimestamp)));
		}

		// Stamped last, with AddOrUpdateProperty - belt and braces on top of the reserved-name skip in
		// BuildProperties, so two independent mechanisms defeat a plugin trying to spoof its identity.
		logEvent.AddOrUpdateProperty(new LogEventProperty(IntegrationLog.IntegrationPropertyName,
			new ScalarValue(pluginId)));

		if (!string.IsNullOrEmpty(dto.SourceContext))
		{
			logEvent.AddOrUpdateProperty(new LogEventProperty(SourceContextPropertyName,
				new ScalarValue(Truncate(dto.SourceContext, ProtocolLimits.MaxLogSourceContextLength))));
		}

		logEvent.AddOrUpdateProperty(new LogEventProperty(SessionIdPropertyName, new ScalarValue(sessionId)));

		if (!string.IsNullOrEmpty(declaredVersion))
		{
			logEvent.AddOrUpdateProperty(new LogEventProperty(PluginVersionPropertyName,
				new ScalarValue(declaredVersion)));
		}

		if (processId is { } pid)
		{
			logEvent.AddOrUpdateProperty(new LogEventProperty(ProcessIdPropertyName, new ScalarValue(pid)));
		}

		return logEvent;
	}

	public static LogEventLevel MapLevel(string? level)
		=> level switch
		{
			LogLevels.Verbose => LogEventLevel.Verbose,
			LogLevels.Debug => LogEventLevel.Debug,
			LogLevels.Information => LogEventLevel.Information,
			LogLevels.Warning => LogEventLevel.Warning,
			LogLevels.Error => LogEventLevel.Error,
			LogLevels.Fatal => LogEventLevel.Fatal,
			_ => LogEventLevel.Information
		};

	private static List<LogEventProperty> BuildProperties(IReadOnlyDictionary<string, string>? properties)
	{
		var result = new List<LogEventProperty>();
		if (properties is null)
		{
			return result;
		}

		var usedNames = new HashSet<string>(StringComparer.Ordinal);

		foreach (var (rawName, rawValue) in properties)
		{
			if (result.Count >= ProtocolLimits.MaxLogPropertiesPerEvent)
			{
				break;
			}

			var name = Truncate(SanitizePropertyName(rawName), ProtocolLimits.MaxLogPropertyNameLength);
			if (name.Length == 0 || _reservedPropertyNames.Contains(name) || !usedNames.Add(name))
			{
				continue;
			}

			result.Add(new LogEventProperty(name,
				new ScalarValue(Truncate(rawValue, ProtocolLimits.MaxLogPropertyValueLength))));
		}

		return result;
	}

	private static string SanitizePropertyName(string name)
	{
		var builder = new StringBuilder(name.Length);
		foreach (var character in name)
		{
			builder.Append(char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_');
		}

		if (builder.Length > 0 && char.IsAsciiDigit(builder[0]))
		{
			builder.Insert(0, '_');
		}

		return builder.ToString();
	}

	private static string ComposeException(LogExceptionDto exception)
	{
		var builder = new StringBuilder();
		AppendException(builder, exception, ProtocolLimits.MaxLogExceptionDepth, 0);
		return Truncate(builder.ToString(), ProtocolLimits.MaxLogExceptionLength);
	}

	private static void AppendException(StringBuilder builder, LogExceptionDto exception, int maxDepth, int depth)
	{
		builder.Append(Truncate(exception.Type, 256))
			.Append(": ")
			.Append(Truncate(exception.Message, ProtocolLimits.MaxLogMessageLength));

		if (!string.IsNullOrEmpty(exception.StackTrace))
		{
			foreach (var line in Truncate(exception.StackTrace, ProtocolLimits.MaxLogExceptionLength).Split('\n'))
			{
				builder.Append('\n').Append("   at ").Append(line.TrimEnd('\r'));
			}
		}

		if (exception.Inner is { } inner && depth + 1 < maxDepth)
		{
			builder.Append('\n').Append(" ---> ");
			AppendException(builder, inner, maxDepth, depth + 1);
			builder.Append('\n').Append("   --- End of inner exception stack trace ---");
		}
	}

	private static string Truncate(string? value, int maxLength)
		=> value is null ? string.Empty : value.Length <= maxLength ? value : value[..maxLength];
}

public sealed class ForwardedPluginException : Exception
{
	private string? _composedText;

	public ForwardedPluginException()
	{
	}

	public ForwardedPluginException(string message)
		: base(message)
	{
	}

	public ForwardedPluginException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	internal static ForwardedPluginException FromComposedText(string composedText)
		=> new(composedText) { _composedText = composedText };

	public override string ToString() => _composedText ?? base.ToString();
}
