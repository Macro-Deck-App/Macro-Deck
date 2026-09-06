using System.Text;
using MacroDeck.Sdk.Logging;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Logging;

public sealed class LogOriginEnricher : ILogEventEnricher
{
	public const string OriginPropertyName = "LogOrigin";
	public const string HostOrigin = "Host";
	public const string IntegrationPrefix = "Integration";

	private const string SourceContextProperty = "SourceContext";

	public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
	{
		ArgumentNullException.ThrowIfNull(logEvent);
		ArgumentNullException.ThrowIfNull(propertyFactory);

		var integrationId = ReadString(logEvent, IntegrationLog.IntegrationPropertyName);
		var category = ShortCategory(ReadString(logEvent, SourceContextProperty));

		var origin = new StringBuilder();
		if (integrationId is null)
		{
			origin.Append(HostOrigin);
		}
		else
		{
			origin.Append(IntegrationPrefix).Append('/').Append(Sanitize(integrationId));
		}

		if (category is not null)
		{
			origin.Append('/').Append(Sanitize(category));
		}

		logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty(OriginPropertyName, origin.ToString()));
	}

	public static string Sanitize(string value)
	{
		var builder = new StringBuilder(value.Length);
		foreach (var character in value)
		{
			builder.Append(char.IsAsciiLetterOrDigit(character) || character is '.' or '_' or '-'
				? character
				: '_');
		}

		return builder.ToString();
	}

	private static string? ShortCategory(string? sourceContext)
	{
		if (string.IsNullOrEmpty(sourceContext))
		{
			return null;
		}

		var lastDot = sourceContext.LastIndexOf('.');

		return lastDot >= 0 && lastDot < sourceContext.Length - 1 ? sourceContext[(lastDot + 1)..] : sourceContext;
	}

	private static string? ReadString(LogEvent logEvent, string propertyName)
		=> logEvent.Properties.TryGetValue(propertyName, out var value) && value is ScalarValue { Value: string text }
			? text
			: null;
}
