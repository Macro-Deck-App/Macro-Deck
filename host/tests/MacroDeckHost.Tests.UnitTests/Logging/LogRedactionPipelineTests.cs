using System.Globalization;
using MacroDeckHost.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Parsing;

namespace MacroDeckHost.Tests.UnitTests.Logging;

[TestFixture]
public class LogRedactionPipelineTests
{
	private const string Jwt = "eyJhbGciOiJIUzI1NiJ9.eyJzY29wZSI6ImFkbWluIn0.dBjftJeZ4CVP-mB92K27uhbUJU1p";

	private static readonly string[] ForeignFiles = ["/home/other-user/a.json", "/home/other-user/b.json"];

	private static readonly string[] ExpectedPropertyNames = ["Count", "Path", "At"];

	[Test]
	public void AspNetCore_RequestLog_QueryString_Is_Redacted_In_The_Rendered_Message()
	{
		var sink = new CapturingSink();
		using var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Redacted(sinks => sinks.Sink(sink))
			.CreateLogger();

		logger.Information("Request starting {Protocol} {Method} {Scheme}://{Host}{PathBase}{Path}{QueryString}",
			"HTTP/1.1",
			"GET",
			"http",
			"127.0.0.1:5191",
			string.Empty,
			"/hubs/ui",
			$"?id=Vf3d&access_token={Jwt}");

		var rendered = sink.Single.RenderMessage(CultureInfo.InvariantCulture);
		Assert.Multiple(() =>
		{
			Assert.That(rendered, Does.Not.Contain(Jwt));
			Assert.That(rendered, Does.Contain("?id=Vf3d&access_token=***"));
			Assert.That(rendered, Does.Contain("/hubs/ui"));
		});
	}

	[Test]
	public void Structured_Property_Values_Are_Redacted()
	{
		var sink = new CapturingSink();
		using var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Redacted(sinks => sinks.Sink(sink))
			.CreateLogger();

		logger.Warning("Failed to run command: {Command}", "curl -H \"Authorization: Bearer abcdefghijkl\" https://x");

		var value = ((ScalarValue)sink.Single.Properties["Command"]).Value as string;
		Assert.Multiple(() =>
		{
			Assert.That(value, Does.Not.Contain("abcdefghijkl"));
			Assert.That(value, Does.Contain("https://x"));
		});
	}

	[Test]
	public void Exception_Text_Is_Redacted_Before_It_Reaches_A_Sink()
	{
		var sink = new CapturingSink();
		using var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Redacted(sinks => sinks.Sink(sink))
			.CreateLogger();

		logger.Error(new InvalidOperationException("GET http://127.0.0.1:8193/callback?code=AQD5x failed"),
			"Callback failed");

		var exception = sink.Single.Exception!.ToString();
		Assert.Multiple(() =>
		{
			Assert.That(exception, Does.Not.Contain("AQD5x"));
			Assert.That(exception, Does.Contain("?code=***"));
			Assert.That(sink.Single.RenderMessage(CultureInfo.InvariantCulture), Is.EqualTo("Callback failed"));
		});
	}

	[Test]
	public void A_Message_That_Is_All_Literal_Text_Is_Redacted()
	{
		var sink = new CapturingSink();
		using var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Redacted(sinks => sinks.Sink(sink))
			.CreateLogger();

		logger.Error(new InvalidOperationException("could not read /Users/other-user/token.json"),
			"Wrote cache to C:\\Users\\other-user\\AppData\\Local\\md\\cache.bin");

		Assert.Multiple(() =>
		{
			Assert.That(sink.Single.RenderMessage(CultureInfo.InvariantCulture),
				Is.EqualTo("Wrote cache to C:\\Users\\<user>\\AppData\\Local\\md\\cache.bin"));
			Assert.That(sink.Single.MessageTemplate.Text, Does.Not.Contain("other-user"));
			Assert.That(sink.Single.Exception!.ToString(), Does.Contain("/Users/<user>/token.json"));
		});
	}

	[Test]
	public void Paths_Are_Redacted_In_Property_Values_Too()
	{
		var sink = new CapturingSink();
		using var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Redacted(sinks => sinks.Sink(sink))
			.CreateLogger();

		logger.Information("Loaded {Path} and {Files}",
			"/home/other-user/.config/md.json",
			ForeignFiles);

		var path = ((ScalarValue)sink.Single.Properties["Path"]).Value as string;
		var files = (SequenceValue)sink.Single.Properties["Files"];
		Assert.Multiple(() =>
		{
			Assert.That(path, Is.EqualTo("/home/<user>/.config/md.json"));
			Assert.That(files.Elements, Has.Count.EqualTo(2));
			Assert.That(files.Elements.Select(element => ((ScalarValue)element).Value as string),
				Is.All.Contains("/home/<user>/"));
		});
	}

	[Test]
	public void Redaction_Preserves_Everything_It_Is_Not_There_To_Change()
	{
		var sink = new CapturingSink();
		using var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Redacted(sinks => sinks.Sink(sink))
			.CreateLogger();

		logger.Warning("Imported {Count} widgets from {Path} at {At}",
			42,
			"/home/other-user/profile.json",
			DateTimeOffset.UnixEpoch);

		var redacted = sink.Single;
		Assert.Multiple(() =>
		{
			Assert.That(redacted.Level, Is.EqualTo(LogEventLevel.Warning));
			Assert.That(redacted.Properties.Keys, Is.EquivalentTo(ExpectedPropertyNames));
			Assert.That(((ScalarValue)redacted.Properties["Count"]).Value, Is.EqualTo(42));
			Assert.That(((ScalarValue)redacted.Properties["At"]).Value, Is.EqualTo(DateTimeOffset.UnixEpoch));
			Assert.That(redacted.MessageTemplate.Tokens.OfType<PropertyToken>().Select(token => token.PropertyName),
				Is.EquivalentTo(ExpectedPropertyNames));
		});
	}

	[Test]
	public void An_Event_With_Nothing_To_Redact_Comes_Out_Unchanged()
	{
		var captured = Capture(logger => logger.Information("Widget {Id} updated in folder {Folder}", "4f2a", "91bd"));

		var redacted = RedactingSink.Redact(captured);

		Assert.Multiple(() =>
		{
			Assert.That(redacted.RenderMessage(CultureInfo.InvariantCulture),
				Is.EqualTo(captured.RenderMessage(CultureInfo.InvariantCulture)));
			Assert.That(((ScalarValue)redacted.Properties["Id"]).Value, Is.EqualTo("4f2a"));
			Assert.That(((ScalarValue)redacted.Properties["Folder"]).Value, Is.EqualTo("91bd"));
		});
	}

	[Test]
	public void Redacting_An_Already_Redacted_Event_Changes_Nothing()
	{
		var captured = Capture(logger => logger.Error(new InvalidOperationException("/home/other-user/x.json missing"),
			$"db at /home/other-user/db.sqlite, token {Jwt}"));

		var once = RedactingSink.Redact(captured);
		var twice = RedactingSink.Redact(once);

		Assert.Multiple(() =>
		{
			Assert.That(twice.RenderMessage(CultureInfo.InvariantCulture),
				Is.EqualTo(once.RenderMessage(CultureInfo.InvariantCulture)));
			Assert.That(twice.Exception!.ToString(), Is.EqualTo(once.Exception!.ToString()));
		});
	}

	[Test]
	public void Log_Origin_Survives_Redaction()
	{
		var sink = new CapturingSink();
		using (var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.Enrich.With(new LogOriginEnricher())
			.WriteTo.Redacted(sinks => sinks.Sink(sink))
			.CreateLogger())
		{
			logger.ForContext(MacroDeck.Sdk.Logging.IntegrationLog.IntegrationPropertyName, "home.Users")
				.Information("started");
		}

		var origin = ((ScalarValue)sink.Single.Properties[LogOriginEnricher.OriginPropertyName]).Value as string;
		Assert.That(origin, Is.EqualTo("Integration/home.Users"));
	}

	[Test]
	public void Every_Sink_Behind_The_Wrapper_Receives_The_Same_Redacted_Event()
	{
		var first = new CapturingSink();
		var second = new CapturingSink();
		using (var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Redacted(sinks =>
			{
				sinks.Sink(first);
				sinks.Sink(second);
			})
			.CreateLogger())
		{
			logger.Error(new InvalidOperationException("/home/other-user/x.json missing"),
				$"Import from {{Path}} failed with ?access_token={Jwt}",
				"/home/other-user/profile.json");
		}

		var firstMessage = first.Single.RenderMessage(CultureInfo.InvariantCulture);
		Assert.Multiple(() =>
		{
			Assert.That(firstMessage, Does.Not.Contain("other-user").And.Not.Contain(Jwt));
			Assert.That(first.Single.Exception!.ToString(), Does.Not.Contain("other-user"));
			Assert.That(second.Single.RenderMessage(CultureInfo.InvariantCulture), Is.EqualTo(firstMessage));
			Assert.That(second.Single.Exception!.ToString(), Is.EqualTo(first.Single.Exception!.ToString()));
		});
	}

	private static LogEvent Capture(Action<ILogger> log)
	{
		var sink = new CapturingSink();
		using (var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(sink)
			.CreateLogger())
		{
			log(logger);
		}

		return sink.Single;
	}

	private sealed class CapturingSink : ILogEventSink
	{
		private readonly List<LogEvent> _events = [];

		public LogEvent Single => _events.Single();

		public void Emit(LogEvent logEvent)
		{
			_events.Add(logEvent);
		}
	}
}
