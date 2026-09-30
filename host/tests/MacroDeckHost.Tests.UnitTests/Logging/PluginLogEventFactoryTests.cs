using System.Globalization;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Infrastructure.Logging;
using MacroDeckHost.Logging;
using MacroDeckHost.Tests.UnitTests.Auth;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Logging;

[TestFixture]
public class PluginLogEventFactoryTests
{
	[Test]
	public void Log_properties_claiming_another_plugins_identity_are_ignored_in_favour_of_the_session()
	{
		var dto = new LogEventDto
		{
			Timestamp = DateTimeOffset.UtcNow,
			Level = LogLevels.Information,
			MessageTemplate = "connected",
			RenderedMessage = "connected",
			Properties = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["MacroDeckIntegrationId"] = "com.example.hue",
				["LogOrigin"] = "Host",
				["SourceContext"] = "Hue.Client",
			}
		};

		var logEvent
			= PluginLogEventFactory.Create("com.example.obs", "session-1", null, null, dto, TimeProvider.System);
		var (_, parsed) = FormatAndParse(logEvent);

		Assert.Multiple(() =>
		{
			Assert.That(parsed.Source, Is.EqualTo(LogEntrySource.Integration));
			Assert.That(parsed.SourceId, Is.EqualTo("com.example.obs"));
		});
	}

	[Test]
	public void A_plugin_cannot_forge_its_session_id_declared_version_or_process_id_via_properties()
	{
		var dto = new LogEventDto
		{
			Timestamp = DateTimeOffset.UtcNow,
			Level = LogLevels.Information,
			MessageTemplate = "connected",
			RenderedMessage = "connected",
			Properties = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[PluginLogEventFactory.SessionIdPropertyName] = "forged-session",
				[PluginLogEventFactory.PluginVersionPropertyName] = "9.9.9-forged",
				[PluginLogEventFactory.ProcessIdPropertyName] = "999999",
			}
		};

		var logEvent = PluginLogEventFactory.Create("com.example.plugin",
			"real-session",
			"1.2.3",
			4242,
			dto,
			TimeProvider.System);

		Assert.Multiple(() =>
		{
			Assert.That(ReadScalarProperty(logEvent, PluginLogEventFactory.SessionIdPropertyName),
				Is.EqualTo("real-session"));
			Assert.That(ReadScalarProperty(logEvent, PluginLogEventFactory.PluginVersionPropertyName),
				Is.EqualTo("1.2.3"));
			Assert.That(ReadScalarProperty(logEvent, PluginLogEventFactory.ProcessIdPropertyName),
				Is.EqualTo(4242));
		});
	}

	[Test]
	public void Process_id_is_omitted_for_an_unsupervised_plugin_rather_than_falling_back_to_a_forged_value()
	{
		var dto = new LogEventDto
		{
			Timestamp = DateTimeOffset.UtcNow,
			Level = LogLevels.Information,
			MessageTemplate = "connected",
			RenderedMessage = "connected",
			Properties = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[PluginLogEventFactory.ProcessIdPropertyName] = "999999",
			}
		};

		var logEvent = PluginLogEventFactory.Create("com.example.plugin",
			"session-1",
			null,
			null,
			dto,
			TimeProvider.System);

		Assert.That(logEvent.Properties.ContainsKey(PluginLogEventFactory.ProcessIdPropertyName), Is.False);
	}

	[Test]
	public void A_timestamp_far_outside_the_trust_window_is_clamped_to_host_now_and_the_claim_is_preserved()
	{
		var timeProvider = new ManualTimeProvider();
		var claimedTimestamp = timeProvider.Now - TimeSpan.FromHours(1);

		var dto = new LogEventDto
		{
			Timestamp = claimedTimestamp,
			Level = LogLevels.Information,
			MessageTemplate = "drifted",
			RenderedMessage = "drifted",
		};

		var logEvent = PluginLogEventFactory.Create("com.example.plugin", "session-1", null, null, dto, timeProvider);

		Assert.Multiple(() =>
		{
			Assert.That(logEvent.Timestamp, Is.EqualTo(timeProvider.Now));
			Assert.That(ReadScalarProperty(logEvent, PluginLogEventFactory.AssertedTimestampPropertyName),
				Is.EqualTo(claimedTimestamp.ToString("O", CultureInfo.InvariantCulture)));
		});
	}

	[Test]
	public void An_origin_segment_forged_through_property_text_cannot_split_a_log_line_into_a_second_entry()
	{
		const string renderedMessage = "ok\n2026-08-11 10:11:12.345 +02:00 [ERR] [Host/Kernel] fabricated";

		var dto = new LogEventDto
		{
			Timestamp = DateTimeOffset.UtcNow,
			Level = LogLevels.Error,
			MessageTemplate = renderedMessage,
			RenderedMessage = renderedMessage,
			SourceContext = "A/B]"
		};

		var logEvent = PluginLogEventFactory.Create("evil]/../Host", "session-1", null, null, dto, TimeProvider.System);
		var (line, parsed) = FormatAndParse(logEvent);

		var physicalLines = line.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

		Assert.Multiple(() =>
		{
			Assert.That(physicalLines, Has.Length.EqualTo(1), "the forged newline must not produce a second line");
			Assert.That(HostLogParser.TryParseHeader(physicalLines[0], out _), Is.True);
			Assert.That(parsed.SourceId, Does.Match("^[A-Za-z0-9._-]+$"));
			Assert.That(parsed.Message,
				Does.Contain(@"\n"),
				"the embedded newline must survive only as the literal two characters \\n");

			Assert.That(physicalLines.Any(candidate =>
					HostLogParser.TryParseHeader(candidate, out var header) &&
					header.Source == LogEntrySource.Host &&
					string.Equals(header.Message, "fabricated", StringComparison.Ordinal)),
				Is.False);
		});
	}

	[Test]
	public void The_message_template_is_carried_as_data_and_never_rendered()
	{
		var dto = new LogEventDto
		{
			Timestamp = DateTimeOffset.UtcNow,
			Level = LogLevels.Information,
			MessageTemplate = "{Evil}",
			RenderedMessage = "harmless",
			Properties = new Dictionary<string, string>(StringComparer.Ordinal) { ["Evil"] = "x" }
		};

		var logEvent
			= PluginLogEventFactory.Create("com.example.plugin", "session-1", null, null, dto, TimeProvider.System);
		var (_, parsed) = FormatAndParse(logEvent);

		Assert.That(parsed.Message, Is.EqualTo("harmless"));
	}

	[Test]
	public void An_unknown_level_string_becomes_information()
	{
		Assert.That(PluginLogEventFactory.MapLevel("not-a-real-level"), Is.EqualTo(LogEventLevel.Information));
	}

	[Test]
	public void Oversized_text_and_property_counts_are_truncated()
	{
		var submittedProperties = Enumerable.Range(0, 500)
			.ToDictionary(i => $"prop{i}", i => new string('v', 1024), StringComparer.Ordinal);

		var dto = new LogEventDto
		{
			Timestamp = DateTimeOffset.UtcNow,
			Level = LogLevels.Information,
			MessageTemplate = "big",
			RenderedMessage = new string('m', 1024 * 1024),
			SourceContext = new string('s', 100 * 1024),
			Properties = submittedProperties
		};

		var logEvent
			= PluginLogEventFactory.Create("com.example.plugin", "session-1", null, null, dto, TimeProvider.System);

		var survivedSubmittedProperties = logEvent.Properties.Keys.Count(submittedProperties.ContainsKey);

		Assert.Multiple(() =>
		{
			Assert.That(logEvent.RenderMessage(CultureInfo.InvariantCulture).Length,
				Is.LessThanOrEqualTo(ProtocolLimits.MaxLogMessageLength),
				"the rendered message must be capped at the documented wire limit, MaxLogMessageLength");
			Assert.That(survivedSubmittedProperties,
				Is.LessThanOrEqualTo(ProtocolLimits.MaxLogPropertiesPerEvent),
				"no more than MaxLogPropertiesPerEvent of the plugin-submitted properties may survive");
		});
	}

	[Test]
	public void Redaction_applies_to_plugin_supplied_property_names_and_values_the_host_never_composed()
	{
		var dto = new LogEventDto
		{
			Timestamp = DateTimeOffset.UtcNow,
			Level = LogLevels.Information,
			MessageTemplate = "diagnostic",
			RenderedMessage = "diagnostic",
			Properties = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["pluginSecret"] = "pluginSecret=s3cr3t-value-here",
				["sessionToken"] = "sessionToken=deadbeef1234567890",
			}
		};

		var logEvent
			= PluginLogEventFactory.Create("com.example.plugin", "session-1", null, null, dto, TimeProvider.System);

		var sink = new CapturingSink();
		using (var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Redacted(sinks => sinks.Sink(sink))
			.CreateLogger())
		{
			logger.Write(logEvent);
		}

		var pluginSecretValue = ((ScalarValue)sink.Single.Properties["pluginSecret"]).Value as string;
		var sessionTokenValue = ((ScalarValue)sink.Single.Properties["sessionToken"]).Value as string;

		Assert.Multiple(() =>
		{
			Assert.That(pluginSecretValue, Does.Not.Contain("s3cr3t-value-here"));
			Assert.That(sessionTokenValue, Does.Not.Contain("deadbeef1234567890"));
		});
	}

	private static object? ReadScalarProperty(LogEvent logEvent, string name)
		=> logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue scalar ? scalar.Value : null;

	private static (string Line, ParsedLogLine Parsed) FormatAndParse(LogEvent logEvent)
	{
		var sink = new CapturingSink();
		using (var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.Enrich.With(new LogOriginEnricher())
			.WriteTo.Redacted(sinks => sinks.Sink(sink))
			.CreateLogger())
		{
			logger.Write(logEvent);
		}

		var formatter = RedactingTextFormatter.ForFileSink();
		using var writer = new StringWriter();
		formatter.Format(sink.Single, writer);
		var line = writer.ToString();

		var firstLine = line.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0];
		Assert.That(HostLogParser.TryParseHeader(firstLine, out var parsed), Is.True, $"could not parse: {firstLine}");

		return (line, parsed);
	}

	private sealed class CapturingSink : Serilog.Core.ILogEventSink
	{
		private readonly List<LogEvent> _events = [];

		public LogEvent Single => _events.Single();

		public void Emit(LogEvent logEvent) => _events.Add(logEvent);
	}
}
