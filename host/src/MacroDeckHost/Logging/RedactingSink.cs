using System.Text;
using MacroDeckHost.Application.Logging;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace MacroDeckHost.Logging;

public sealed class RedactingSink(ILogEventSink inner) : ILogEventSink
{
	private readonly ILogEventSink _inner = inner ?? throw new ArgumentNullException(nameof(inner));

	public void Emit(LogEvent logEvent) => _inner.Emit(Redact(logEvent));

	public static LogEvent Redact(LogEvent logEvent)
	{
		ArgumentNullException.ThrowIfNull(logEvent);

		var template = RedactTemplate(logEvent.MessageTemplate, out var templateChanged);
		var properties = RedactMany(logEvent.Properties, out var propertiesChanged);
		var exception = RedactException(logEvent.Exception, out var exceptionChanged);

		if (!templateChanged && !propertiesChanged && !exceptionChanged)
		{
			return logEvent;
		}

		return logEvent.TraceId is null && logEvent.SpanId is null
			? new LogEvent(logEvent.Timestamp, logEvent.Level, exception, template, properties)
			: new LogEvent(logEvent.Timestamp,
				logEvent.Level,
				exception,
				template,
				properties,
				logEvent.TraceId ?? default,
				logEvent.SpanId ?? default);
	}

	private static MessageTemplate RedactTemplate(MessageTemplate template, out bool changed)
	{
		changed = false;
		var tokens = new List<MessageTemplateToken>(template.Tokens.Count());
		foreach (var token in template.Tokens)
		{
			if (token is not TextToken text)
			{
				tokens.Add(token);
				continue;
			}

			var redacted = Sanitize(text.Text);
			if (string.Equals(redacted, text.Text, StringComparison.Ordinal))
			{
				tokens.Add(token);
				continue;
			}

			changed = true;
			tokens.Add(new TextToken(redacted));
		}

		return changed ? new MessageTemplate(ComposeText(tokens), tokens) : template;
	}

	private static string ComposeText(IEnumerable<MessageTemplateToken> tokens)
	{
		var builder = new StringBuilder();
		foreach (var token in tokens)
		{
			if (token is TextToken text)
			{
				builder.Append(text.Text
					.Replace("{", "{{", StringComparison.Ordinal)
					.Replace("}", "}}", StringComparison.Ordinal));
			}
			else
			{
				builder.Append(token);
			}
		}

		return builder.ToString();
	}

	private static Exception? RedactException(Exception? exception, out bool changed)
	{
		changed = false;
		if (exception is null)
		{
			return null;
		}

		var text = exception.ToString();
		var redacted = LogText.NeutralizeControls(LogRedactor.Redact(text))!;
		if (string.Equals(redacted, text, StringComparison.Ordinal))
		{
			return exception;
		}

		changed = true;

		return new RedactedException(redacted);
	}

	private static LogEventPropertyValue Redact(LogEventPropertyValue value)
	{
		switch (value)
		{
			case ScalarValue { Value: string text }:
			{
				var redacted = Sanitize(text);
				return string.Equals(redacted, text, StringComparison.Ordinal) ? value : new ScalarValue(redacted);
			}
			case ScalarValue { Value: char or Uri } scalar:
				return NeutralizeScalar(scalar);
			case SequenceValue sequence:
			{
				var elements = RedactMany(sequence.Elements, out var changed);
				return changed ? new SequenceValue(elements) : value;
			}
			case StructureValue structure:
			{
				var properties = RedactMany(structure.Properties, out var changed);
				return changed ? new StructureValue(properties, structure.TypeTag) : value;
			}
			case DictionaryValue dictionary:
				return RedactDictionary(dictionary);
			default:
				return value;
		}
	}

	private static DictionaryValue RedactDictionary(DictionaryValue dictionary)
	{
		var source = dictionary.Elements.ToList();
		var neutralizedKeys = source
			.Select(entry => entry.Key.Value is string or char or Uri ? NeutralizeKey(entry.Key) : entry.Key)
			.ToList();
		var taken = new HashSet<ScalarValue>();
		for (var i = 0; i < source.Count; i++)
		{
			if (ReferenceEquals(neutralizedKeys[i], source[i].Key))
			{
				taken.Add(neutralizedKeys[i]);
			}
		}

		var changed = false;
		var entries = new List<KeyValuePair<ScalarValue, LogEventPropertyValue>>(source.Count);
		var index = 0;
		foreach (var entry in source)
		{
			var redacted = Redact(entry.Value);
			var key = neutralizedKeys[index++];
			if (!ReferenceEquals(key, entry.Key))
			{
				var text = (string)key.Value!;
				for (var suffix = 2; !taken.Add(key); suffix++)
				{
					key = new ScalarValue($"{text}#{suffix}");
				}
			}

			changed |= !ReferenceEquals(redacted, entry.Value) || !ReferenceEquals(key, entry.Key);
			entries.Add(new KeyValuePair<ScalarValue, LogEventPropertyValue>(key, redacted));
		}

		return changed ? new DictionaryValue(entries) : dictionary;
	}

	// Redact before neutralising: the secret patterns allow whitespace after the separator,
	// which neutralising would rewrite.
	private static string Sanitize(string text) => LogText.Neutralize(LogRedactor.Redact(text))!;

	private static ScalarValue NeutralizeScalar(ScalarValue scalar)
	{
		var text = scalar.Value is Uri uri ? uri.ToString() : scalar.Value!.ToString()!;
		var neutralized = LogText.Neutralize(text)!;
		return string.Equals(neutralized, text, StringComparison.Ordinal) ? scalar : new ScalarValue(neutralized);
	}

	private static ScalarValue NeutralizeKey(ScalarValue key)
		=> key.Value is string text
			? LogText.Neutralize(text) is { } neutralized && !string.Equals(neutralized, text, StringComparison.Ordinal)
				? new ScalarValue(neutralized)
				: key
			: NeutralizeScalar(key);

	private static List<LogEventPropertyValue> RedactMany(
		IReadOnlyList<LogEventPropertyValue> values,
		out bool changed)
	{
		changed = false;
		var result = new List<LogEventPropertyValue>(values.Count);
		foreach (var value in values)
		{
			var redacted = Redact(value);
			changed |= !ReferenceEquals(redacted, value);
			result.Add(redacted);
		}

		return result;
	}

	private static List<LogEventProperty> RedactMany(IReadOnlyList<LogEventProperty> properties, out bool changed)
	{
		changed = false;
		var result = new List<LogEventProperty>(properties.Count);
		foreach (var property in properties)
		{
			var redacted = Redact(property.Value);
			changed |= !ReferenceEquals(redacted, property.Value);
			result.Add(new LogEventProperty(property.Name, redacted));
		}

		return result;
	}

	private static List<LogEventProperty> RedactMany(
		IReadOnlyDictionary<string, LogEventPropertyValue> properties,
		out bool changed)
	{
		changed = false;
		var result = new List<LogEventProperty>(properties.Count);
		foreach (var property in properties)
		{
			var redacted = Redact(property.Value);
			changed |= !ReferenceEquals(redacted, property.Value);
			result.Add(new LogEventProperty(property.Key, redacted));
		}

		return result;
	}
}

internal sealed class RedactedException(string text) : Exception(text)
{
	private readonly string _text = text;

	public override string ToString() => _text;
}
