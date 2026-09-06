using MacroDeck.Plugin.Protocol.Logging;
using MacroDeckHost.Application.Logging;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Extensions;
using MacroDeckHost.Infrastructure.Logging;
using MacroDeckHost.Logging;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Logging;

public class PluginLogFileIntegrationTests
{
	private const string Token = "eyJhbGciOiJIUzI1NiJ9.eyJhIjoxfQ.dBjftJeZ4CVP-mB92K27uhbUJU1p";

	private TestPaths _paths = null!;
	private ILogger _previousLogger = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_previousLogger = Log.Logger;
	}

	[TearDown]
	public void TearDown()
	{
		Log.Logger = _previousLogger;
		_paths.Cleanup();
	}

	[Test]
	public async Task A_forwarded_events_credential_bearing_property_and_exception_text_never_reach_the_file()
	{
		const string pluginId = "com.example.obs";

		var host = await new HostBuilder()
			.ConfigureSerilog(_paths, new LogLevelState(LogEntryLevel.Information))
			.StartAsync();

		using (host)
		{
			var dto = new LogEventDto
			{
				Timestamp = DateTimeOffset.UtcNow,
				Level = LogLevels.Error,
				MessageTemplate = "Callback failed",
				RenderedMessage = "Callback failed",
				SourceContext = "MacroDeckObs.Client",
				Properties = new Dictionary<string, string>(StringComparer.Ordinal)
				{
					["request"] = $"access_token={Token}"
				},
				Exception = new LogExceptionDto
				{
					Type = "System.InvalidOperationException",
					Message = $"GET http://127.0.0.1:8193/callback?access_token={Token} failed",
					StackTrace = "at MacroDeckObs.Client.Connect()\nat MacroDeckObs.Client.Run()"
				}
			};

			var logEvent = PluginLogEventFactory.Create(pluginId, "session-1", "1.2.3", 4242, dto, TimeProvider.System);
			Log.Logger.Write(logEvent);

			await Log.CloseAndFlushAsync();
		}

		var contents = string.Concat(Directory.EnumerateFiles(_paths.LogsDirectory, "host-*.log")
			.Select(File.ReadAllText));

		Assert.That(contents, Is.Not.Empty, "the forwarded event should have reached the file sink");
		Assert.That(contents, Does.Not.Contain(Token), "the raw token must appear nowhere in the file");
		Assert.That(contents, Does.Contain("access_token=***"));
		Assert.That(contents, Does.Contain("[Integration/com.example.obs/Client]"));

		var lines = contents.Split('\n').Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0).ToList();
		var headerIndex = lines.FindIndex(line =>
			HostLogParser.TryParseHeader(line, out var candidate) &&
			string.Equals(candidate.Message, "Callback failed", StringComparison.Ordinal));
		Assert.That(headerIndex, Is.GreaterThanOrEqualTo(0), "the forwarded event's header line should parse");

		Assert.That(HostLogParser.TryParseHeader(lines[headerIndex], out var header), Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(header.Source, Is.EqualTo(LogEntrySource.Integration));
			Assert.That(header.SourceId, Is.EqualTo(pluginId));
			Assert.That(header.Category, Is.EqualTo("Client"));
			Assert.That(header.Message, Is.EqualTo("Callback failed"));
		});

		// Every following line up to the next header (or EOF) is the exception block - it must stay
		// indented, i.e. never itself parse as a header, so it cannot be read back as a second entry.
		for (var i = headerIndex + 1; i < lines.Count; i++)
		{
			Assert.That(HostLogParser.TryParseHeader(lines[i], out _),
				Is.False,
				$"exception continuation line parsed as a header: {lines[i]}");
			Assert.That(lines[i], Does.StartWith(RedactingTextFormatter.ContinuationIndent));
		}
	}

	[Test]
	public void The_events_property_values_are_also_redacted_in_a_capturing_sink_not_just_the_file_line()
	{
		var dto = new LogEventDto
		{
			Timestamp = DateTimeOffset.UtcNow,
			Level = LogLevels.Warning,
			MessageTemplate = "diagnostic",
			RenderedMessage = "diagnostic",
			Properties = new Dictionary<string, string>(StringComparer.Ordinal)
			{
				["detail"] = $"access_token={Token}"
			}
		};

		var logEvent
			= PluginLogEventFactory.Create("com.example.obs", "session-1", null, null, dto, TimeProvider.System);

		var sink = new CapturingSink();
		using (var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Redacted(sinks => sinks.Sink(sink))
			.CreateLogger())
		{
			logger.Write(logEvent);
		}

		var value = ((ScalarValue)sink.Single.Properties["detail"]).Value as string;
		Assert.That(value, Does.Not.Contain(Token));
	}

	[Test]
	public void A_forwarded_exception_block_stays_indented()
	{
		var dto = new LogEventDto
		{
			Timestamp = DateTimeOffset.UtcNow,
			Level = LogLevels.Error,
			MessageTemplate = "failed",
			RenderedMessage = "failed",
			Exception = new LogExceptionDto
			{
				Type = "System.Exception",
				Message = "boom\n2026-08-11 10:11:12.345 +02:00 [ERR] [Host/Kernel] fabricated",
				StackTrace = "at A()\n2026-08-11 10:11:12.345 +02:00 [ERR] [Host/Kernel] fabricated2"
			}
		};

		var logEvent
			= PluginLogEventFactory.Create("com.example.obs", "session-1", null, null, dto, TimeProvider.System);

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

		var lines = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

		Assert.Multiple(() =>
		{
			Assert.That(HostLogParser.TryParseHeader(lines[0], out _), Is.True);

			for (var i = 1; i < lines.Length; i++)
			{
				Assert.That(HostLogParser.TryParseHeader(lines[i], out _),
					Is.False,
					$"line {i} of the exception block parsed as a header: {lines[i]}");
				Assert.That(lines[i], Does.StartWith(RedactingTextFormatter.ContinuationIndent));
			}
		});
	}

	private sealed class CapturingSink : Serilog.Core.ILogEventSink
	{
		private readonly List<LogEvent> _events = [];

		public LogEvent Single => _events.Single();

		public void Emit(LogEvent logEvent) => _events.Add(logEvent);
	}
}
