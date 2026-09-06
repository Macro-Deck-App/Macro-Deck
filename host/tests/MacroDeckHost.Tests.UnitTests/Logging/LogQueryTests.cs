using MacroDeckHost.Application.Logging;
using MacroDeckHost.Infrastructure.Logging;
using MacroDeckHost.Logging;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Logging;

[TestFixture]
public class LogQueryTests
{
	private static readonly DateTimeOffset _noon = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

	[Test]
	public void IntegrationId_Matches_Only_That_Plugins_Entry_Source_Matches_Both_And_All_Matches_Everything()
	{
		var host = BuildHostEntry();
		var bootstrapper = BuildBootstrapperEntry();
		var pluginA = BuildIntegrationEntry("com.example.a");
		var pluginB = BuildIntegrationEntry("com.example.b");

		var entries = new[] { host, bootstrapper, pluginA, pluginB };

		var byIntegrationId = new LogQuery { IntegrationId = "com.example.a" };
		var bySource = new LogQuery { Source = LogEntrySource.Integration };

		Assert.Multiple(() =>
		{
			Assert.That(entries.Where(entry => byIntegrationId.Matches(entry, LogEntryLevel.Verbose)),
				Is.EqualTo(new[] { pluginA }));
			Assert.That(entries.Where(entry => bySource.Matches(entry, LogEntryLevel.Verbose)),
				Is.EquivalentTo(new[] { pluginA, pluginB }));
			Assert.That(entries.Where(entry => LogQuery.All.Matches(entry, LogEntryLevel.Verbose)),
				Is.EquivalentTo(entries));
		});
	}

	[Test]
	public void Level_Source_Search_And_Range_Combine_As_A_Conjunction()
	{
		var query = new LogQuery
		{
			Levels = [LogEntryLevel.Error],
			IntegrationId = "obs",
			Search = "needle",
			From = _noon,
			To = _noon.AddMinutes(10)
		};

		var wrongLevel = Entry(LogEntryLevel.Warning, "obs", "needle here", _noon.AddMinutes(1));
		var wrongSource = Entry(LogEntryLevel.Error, "spotify", "needle here", _noon.AddMinutes(1));
		var wrongSearch = Entry(LogEntryLevel.Error, "obs", "nothing here", _noon.AddMinutes(1));
		var outsideRange = Entry(LogEntryLevel.Error, "obs", "needle here", _noon.AddMinutes(30));
		var matching = Entry(LogEntryLevel.Error, "obs", "needle here", _noon.AddMinutes(5));

		Assert.Multiple(() =>
		{
			Assert.That(query.Matches(matching, LogEntryLevel.Verbose), Is.True);
			Assert.That(query.Matches(wrongLevel, LogEntryLevel.Verbose), Is.False, "the level facet must apply");
			Assert.That(query.Matches(wrongSource, LogEntryLevel.Verbose), Is.False, "the source facet must apply");
			Assert.That(query.Matches(wrongSearch, LogEntryLevel.Verbose), Is.False, "the search facet must apply");
			Assert.That(query.Matches(outsideRange, LogEntryLevel.Verbose), Is.False, "the range facet must apply");
		});
	}

	[Test]
	public void A_Range_Includes_Both_Endpoints()
	{
		var query = new LogQuery { From = _noon, To = _noon.AddMinutes(10) };

		Assert.Multiple(() =>
		{
			Assert.That(query.Matches(Entry(timestamp: _noon), LogEntryLevel.Verbose), Is.True);
			Assert.That(query.Matches(Entry(timestamp: _noon.AddMinutes(10)), LogEntryLevel.Verbose), Is.True);
			Assert.That(query.Matches(Entry(timestamp: _noon.AddTicks(-1)), LogEntryLevel.Verbose), Is.False);
			Assert.That(query.Matches(Entry(timestamp: _noon.AddMinutes(10).AddTicks(1)), LogEntryLevel.Verbose),
				Is.False);
		});
	}

	[Test]
	public void A_Search_Matches_The_Logger_Name_And_The_Integration_Id_Without_Crossing_Over()
	{
		var obs = Entry(integrationId: "obs", message: "scene changed", category: "MacroDeckHost.Integrations.Obs");
		var spotify = Entry(integrationId: "spotify", message: "track changed", category: "SpotifyMusicPlayer");

		Assert.Multiple(() =>
		{
			Assert.That(new LogQuery { Search = "obs" }.Matches(obs, LogEntryLevel.Verbose), Is.True);
			Assert.That(new LogQuery { Search = "obs" }.Matches(spotify, LogEntryLevel.Verbose), Is.False);
			Assert.That(new LogQuery { Search = "SpotifyMusicPlayer" }.Matches(spotify, LogEntryLevel.Verbose),
				Is.True);
			Assert.That(new LogQuery { Search = "SpotifyMusicPlayer" }.Matches(obs, LogEntryLevel.Verbose), Is.False);
		});
	}

	private static LogEntry Entry(
		LogEntryLevel level = LogEntryLevel.Information,
		string? integrationId = null,
		string message = "message",
		DateTimeOffset? timestamp = null,
		string? category = null)
		=> new()
		{
			Id = LogEntryId.Create(LogFileKind.Host, "20260101", 0),
			Timestamp = timestamp ?? _noon,
			Level = level,
			Source = integrationId is null ? LogEntrySource.Host : LogEntrySource.Integration,
			SourceId = integrationId,
			Category = category,
			Message = message
		};

	private static LogEntry BuildHostEntry() => FormatAndParseHostEntry(null, "host message");

	private static LogEntry BuildIntegrationEntry(string pluginId) =>
		FormatAndParseHostEntry(pluginId, "plugin message");

	private static LogEntry FormatAndParseHostEntry(string? integrationId, string message)
	{
		var properties = new List<LogEventProperty>();
		if (integrationId is not null)
		{
			properties.Add(new LogEventProperty(MacroDeck.Sdk.Logging.IntegrationLog.IntegrationPropertyName,
				new ScalarValue(integrationId)));
		}

		var logEvent = new LogEvent(DateTimeOffset.UtcNow,
			LogEventLevel.Information,
			null,
			new MessageTemplate([new Serilog.Parsing.TextToken(message)]),
			properties);

		var sink = new CapturingSink();
		using (var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.Enrich.With(new LogOriginEnricher())
			.WriteTo.Sink(sink)
			.CreateLogger())
		{
			logger.Write(logEvent);
		}

		var formatter = RedactingTextFormatter.ForFileSink();
		using var writer = new StringWriter();
		formatter.Format(sink.Single, writer);
		var line = writer.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries)[0];

		Assert.That(HostLogParser.TryParseHeader(line, out var parsed), Is.True);

		return new LogEntry
		{
			Id = LogEntryId.Create(LogFileKind.Host, "20260101", 0),
			Timestamp = parsed.Timestamp,
			Level = parsed.Level,
			Source = parsed.Source,
			SourceId = parsed.SourceId,
			Category = parsed.Category,
			Message = parsed.Message
		};
	}

	private static LogEntry BuildBootstrapperEntry()
	{
		const string line = "[10:11:12 INF] bootstrapper message";
		var date = new DateOnly(2026, 1, 1);

		Assert.That(BootstrapperLogParser.TryParseHeader(line, date, out var parsed), Is.True);

		return new LogEntry
		{
			Id = LogEntryId.Create(LogFileKind.Bootstrapper, "20260101", 0),
			Timestamp = parsed.Timestamp,
			Level = parsed.Level,
			Source = parsed.Source,
			SourceId = parsed.SourceId,
			Category = parsed.Category,
			Message = parsed.Message
		};
	}

	private sealed class CapturingSink : ILogEventSink
	{
		private readonly List<LogEvent> _events = [];

		public LogEvent Single => _events.Single();

		public void Emit(LogEvent logEvent) => _events.Add(logEvent);
	}
}
