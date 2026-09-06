using System.Globalization;
using MacroDeck.Plugin.Serilog;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Infrastructure.Logging;
using MacroDeckHost.Logging;
using MacroDeckHost.Tests.PluginContractTests.Harness;
using MacroDeck.Sdk.Logging;
using Microsoft.Extensions.Options;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class LoggingContractTests : CapabilityContractFixture
{
	private CollectingSink _hostSink = null!;
	private Logger _hostLogger = null!;
	private PluginLogIngestor _ingestor = null!;

	[SetUp]
	public void LoggingSetUp()
	{
		_hostSink = new CollectingSink();

		// Mirrors the host's real pipeline (see Program.cs) closely enough for this contract:
		// LogOriginEnricher is what RedactingTextFormatter reads for attribution, and that enricher is
		// exactly what test 2 below is proving a plugin cannot forge.
		_hostLogger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.Enrich.With(new LogOriginEnricher())
			.WriteTo.Sink(_hostSink)
			.CreateLogger();

		_ingestor = new PluginLogIngestor(new PluginLogRateLimiter(TimeProvider.System),
			SessionRegistry,
			new EmptyPluginSupervisor(),
			TimeProvider.System,
			() => _hostLogger);
	}

	[TearDown]
	public void LoggingTearDown() => _hostLogger.Dispose();

	[Test]
	public async Task A_plugin_serilog_event_reaches_the_host_with_every_supported_field()
	{
		await ConnectAsync([], [], []);
		Link.LogPublishHandler = (payload, _) =>
		{
			_ingestor.Ingest(PluginId, SessionId, payload.Events);
			return Task.CompletedTask;
		};

		var pluginSink = new MacroDeckLogSink(Options.Create(new MacroDeckLoggingOptions()));

		var inner = new FormatException("inner boom");
		var outer = new InvalidOperationException("outer boom", inner);

		// A fixed, non-UTC offset, kept within PluginLogEventFactory's five-minute trust window (it
		// compares absolute instants, never the offset) so the timestamp round-trips unclamped and its
		// offset survives the JSON envelope this test actually sends over the link.
		var timestamp = TimeProvider.System.GetUtcNow().ToOffset(TimeSpan.FromHours(5));

		using var binder = new LoggerConfiguration().CreateLogger();
		var bound = binder.BindMessageTemplate("Scene {Scene} switched after {Ms} ms",
			["main", 42],
			out var template,
			out var properties);
		Assert.That(bound, Is.True, "Sanity check: Serilog's own template binder rejected this template.");

		pluginSink.Emit(new LogEvent(timestamp, LogEventLevel.Warning, outer, template!, properties ?? []));

		var drained = pluginSink.Drain(10);
		var dtos = drained.Events.Select(LogEventProjection.ToDto).ToList();
		await SendLogPublishFromPluginAsync(dtos, drained.Dropped > 0 ? drained.Dropped : null);

		await WaitForAsync(() => _hostSink.Events.Count > 0, "The host never ingested the log event.");

		var hostEvent = _hostSink.Events.Single();

		Assert.Multiple(() =>
		{
			Assert.That(hostEvent.Level, Is.EqualTo(LogEventLevel.Warning));

			Assert.That(hostEvent.MessageTemplate.Text, Is.EqualTo("Scene \"main\" switched after 42 ms"));
			Assert.That(hostEvent.RenderMessage(CultureInfo.InvariantCulture),
				Is.EqualTo("Scene \"main\" switched after 42 ms"));

			Assert.That(ReadProperty(hostEvent, "Scene"), Is.EqualTo("main"));
			Assert.That(ReadProperty(hostEvent, "Ms"), Is.EqualTo("42"));

			Assert.That(hostEvent.Timestamp, Is.EqualTo(timestamp));
			Assert.That(hostEvent.Timestamp.Offset, Is.EqualTo(timestamp.Offset));

			Assert.That(hostEvent.Exception, Is.Not.Null);
			var exceptionText = hostEvent.Exception!.ToString();
			Assert.That(exceptionText, Does.Contain(nameof(InvalidOperationException)));
			Assert.That(exceptionText, Does.Contain("outer boom"));
			Assert.That(exceptionText, Does.Contain(nameof(FormatException)));
			Assert.That(exceptionText, Does.Contain("inner boom"));
		});
	}

	[Test]
	public async Task A_plugin_using_ForContext_cannot_change_its_own_attribution()
	{
		await ConnectAsync([], [], []);
		Link.LogPublishHandler = (payload, _) =>
		{
			_ingestor.Ingest(PluginId, SessionId, payload.Events);
			return Task.CompletedTask;
		};

		var pluginSink = new MacroDeckLogSink(Options.Create(new MacroDeckLoggingOptions()));
		using var pluginLogger = new LoggerConfiguration().WriteTo.Sink(pluginSink).CreateLogger();

		pluginLogger.ForContext(IntegrationLog.IntegrationPropertyName, "com.example.other")
			.Information("Attempted spoof");

		var dtos = pluginSink.Drain(10).Events.Select(LogEventProjection.ToDto).ToList();
		await SendLogPublishFromPluginAsync(dtos);

		await WaitForAsync(() => _hostSink.Events.Count > 0, "The host never ingested the log event.");

		var hostEvent = _hostSink.Events.Single();

		Assert.That(ReadProperty(hostEvent, IntegrationLog.IntegrationPropertyName), Is.EqualTo(PluginId));

		var headerLine = FormatHeaderLine(hostEvent);
		var parsed = HostLogParser.TryParseHeader(headerLine, out var parsedLine);

		Assert.Multiple(() =>
		{
			Assert.That(parsed, Is.True);
			Assert.That(parsedLine.Source, Is.EqualTo(LogEntrySource.Integration));
			Assert.That(parsedLine.SourceId, Is.EqualTo(PluginId));
		});
	}

	private static string? ReadProperty(LogEvent logEvent, string name)
		=> logEvent.Properties.TryGetValue(name, out var value) && value is ScalarValue { Value: string text }
			? text
			: null;

	private static string FormatHeaderLine(LogEvent logEvent)
	{
		using var writer = new StringWriter();
		RedactingTextFormatter.ForFileSink().Format(logEvent, writer);
		return writer.ToString().Split('\n')[0].TrimEnd('\r');
	}

	private sealed class CollectingSink : ILogEventSink
	{
		private readonly object _gate = new();
		private readonly List<LogEvent> _events = [];

		public List<LogEvent> Events
		{
			get
			{
				lock (_gate)
				{
					return _events.ToList();
				}
			}
		}

		public void Emit(LogEvent logEvent)
		{
			lock (_gate)
			{
				_events.Add(logEvent);
			}
		}
	}
}
