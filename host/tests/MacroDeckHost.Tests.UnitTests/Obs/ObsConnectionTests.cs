using System.Diagnostics;
using MacroDeckHost.Integrations.Obs;
using Serilog;
using Serilog.Core;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Obs;

[TestFixture]
internal sealed class ObsConnectionTests
{
	[Test]
	public void StateChangedEvent_RefreshesSnapshot()
	{
		var fake = new FakeObsClient
		{
			IsConnected = true,
			Status = new ObsStatus { CurrentScene = "Live", IsRecording = true }
		};
		using var connection = new ObsConnection(fake, "ws://localhost:4455", null);

		fake.RaiseStateChanged();

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.IsConnected, Is.True);
			Assert.That(connection.State.CurrentScene, Is.EqualTo("Live"));
			Assert.That(connection.State.IsRecording, Is.True);
		});
	}

	[Test]
	public void ConnectedEvent_PopulatesStateFromStatus()
	{
		var fake = new FakeObsClient { Status = new ObsStatus { CurrentScene = "Intro" } };
		using var connection = new ObsConnection(fake, "ws://localhost:4455", null);

		fake.IsConnected = true;
		fake.RaiseConnected();

		Assert.That(connection.State.CurrentScene, Is.EqualTo("Intro"));
	}

	[Test]
	public async Task Disconnected_SchedulesReconnect()
	{
		var fake = new FakeObsClient();
		using var connection = new ObsConnection(fake,
			"ws://localhost:4455",
			null,
			reconnectDelay: TimeSpan.FromMilliseconds(50));

		connection.Start();
		Assert.That(fake.ConnectCount, Is.EqualTo(1));

		fake.RaiseDisconnected("dropped");

		await WaitForAsync(() => fake.ConnectCount >= 2);
		Assert.That(fake.ConnectCount, Is.GreaterThanOrEqualTo(2));
	}

	[Test]
	public void Dispose_ClearsStateAndDoesNotReconnect()
	{
		var fake = new FakeObsClient
		{
			IsConnected = true,
			Status = new ObsStatus { CurrentScene = "Live" }
		};
		var connection = new ObsConnection(fake,
			"ws://localhost:4455",
			null,
			reconnectDelay: TimeSpan.FromMilliseconds(50));
		fake.RaiseStateChanged();
		Assert.That(connection.State.IsConnected, Is.True);

		connection.Dispose();

		// A disconnect after disposal must not trigger another connect attempt.
		fake.RaiseDisconnected("after dispose");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.IsConnected, Is.False);
			Assert.That(fake.ConnectCount, Is.EqualTo(0));
		});
	}

	[Test]
	public void DisconnectedEvents_BeforeReconnecting_LogOnceUntilTheNextConnection()
	{
		var fake = new FakeObsClient();
		var sink = new CollectingSink();
		var logger = new LoggerConfiguration()
			.MinimumLevel.Verbose()
			.WriteTo.Sink(sink)
			.CreateLogger();
		using var connection = new ObsConnection(fake,
			"ws://localhost:4455",
			null,
			reconnectDelay: TimeSpan.FromSeconds(10),
			logger: logger);

		fake.RaiseDisconnected("unreachable");
		fake.RaiseDisconnected("unreachable");

		fake.IsConnected = true;
		fake.RaiseConnected();
		fake.IsConnected = false;
		fake.RaiseDisconnected("stopped");

		Assert.That(sink.Events.Count(logEvent => logEvent.MessageTemplate.Text.StartsWith("OBS disconnected",
				StringComparison.Ordinal)),
			Is.EqualTo(2));
	}

	[Test]
	public async Task A_Long_Outage_Says_So_Once_And_Keeps_Every_Retry_At_Debug()
	{
		var fake = new FakeObsClient();
		var sink = new CollectingSink();
		using var connection = Connected(fake, sink);

		await FailFrom(fake, connection, attempts: 50);

		Assert.Multiple(() =>
		{
			Assert.That(sink.Events.Count(logEvent => logEvent.Level >= LogEventLevel.Warning),
				Is.EqualTo(1),
				"an outage is one event, however long it lasts");
			Assert.That(sink.Events.Count(logEvent => logEvent.Level == LogEventLevel.Information),
				Is.EqualTo(1),
				"and the only Information line is the connection it started from");
		});
	}

	[Test]
	public async Task Debug_Still_Shows_Every_Single_Retry_Attempt()
	{
		var fake = new FakeObsClient();
		var sink = new CollectingSink();
		using var connection = Connected(fake, sink);

		await FailFrom(fake, connection, attempts: 20);

		var attempts = sink.Events
			.Where(logEvent => logEvent.MessageTemplate.Text.StartsWith("OBS connecting to",
				StringComparison.Ordinal))
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(attempts, Has.Count.GreaterThanOrEqualTo(20));
			Assert.That(attempts.TrueForAll(logEvent => logEvent.Level == LogEventLevel.Debug));
		});
	}

	[Test]
	public async Task An_Outage_And_Its_Recovery_Are_Both_Visible()
	{
		var fake = new FakeObsClient();
		var sink = new CollectingSink();
		using var connection = Connected(fake, sink);

		await FailFrom(fake, connection, attempts: 5);
		fake.ConnectRaisesConnected = true;
		fake.ConnectRaisesDisconnected = false;
		await WaitForAsync(() => Loud(sink).Count >= 3, TimeSpan.FromSeconds(15));

		fake.ConnectRaisesDisconnected = true;
		fake.ConnectRaisesConnected = false;
		fake.IsConnected = false;
		fake.RaiseDisconnected("dropped again");
		await WaitForAsync(() => Loud(sink).Count >= 4, TimeSpan.FromSeconds(15));

		(LogEventLevel Level, string Template)[] expected =
		[
			(LogEventLevel.Information, "OBS connected to {Url}"),
			(LogEventLevel.Warning, "OBS connection lost: {LastError}"),
			(LogEventLevel.Information, "OBS reconnected to {Url} after {Duration} and {Attempts} attempt(s)"),
			(LogEventLevel.Warning, "OBS connection lost: {LastError}")
		];
		Assert.That(Loud(sink).Select(logEvent => (logEvent.Level, logEvent.MessageTemplate.Text)),
			Is.EqualTo(expected));
	}

	[Test]
	public async Task An_Outage_That_Never_Connected_Keeps_Its_Repeating_Summary_Below_Warning()
	{
		var fake = new FakeObsClient { ConnectRaisesDisconnected = true, DisconnectReason = "connection refused" };
		var sink = new CollectingSink();
		using var connection = Summarizing(fake, sink);

		await WaitForAsync(() => Summaries(sink).Count >= 2, TimeSpan.FromSeconds(15));

		Assert.That(Summaries(sink), Has.Count.GreaterThanOrEqualTo(2), "the summary has to repeat to be tested");
		Assert.Multiple(() =>
		{
			Assert.That(Summaries(sink).TrueForAll(logEvent => logEvent.Level == LogEventLevel.Information));
			Assert.That(sink.Events.Where(logEvent => logEvent.Level >= LogEventLevel.Warning),
				Is.Empty,
				"an OBS that was never started is not a fault to warn about");
		});
	}

	[Test]
	public async Task An_Outage_That_Lost_An_Established_Connection_Keeps_Its_Summary_At_Warning()
	{
		var fake = new FakeObsClient
			{ ConnectRaisesConnected = true, Status = new ObsStatus { CurrentScene = "Live" } };
		var sink = new CollectingSink();
		using var connection = Summarizing(fake, sink);
		await WaitForAsync(() => connection.Status == ObsConnectionStatus.Connected, TimeSpan.FromSeconds(5));

		fake.ConnectRaisesConnected = false;
		fake.ConnectRaisesDisconnected = true;
		fake.DisconnectReason = "connection refused";
		fake.IsConnected = false;
		fake.RaiseDisconnected("connection refused");

		await WaitForAsync(() => Summaries(sink).Count >= 2, TimeSpan.FromSeconds(15));

		Assert.That(Summaries(sink), Has.Count.GreaterThanOrEqualTo(2), "the summary has to repeat to be tested");
		Assert.That(Summaries(sink).TrueForAll(logEvent => logEvent.Level == LogEventLevel.Warning));
	}

	private static List<LogEvent> Summaries(CollectingSink sink)
		=> sink.Events.Where(logEvent => logEvent.MessageTemplate.Text.StartsWith("OBS has been unreachable",
				StringComparison.Ordinal))
			.ToList();

	private static ObsConnection Summarizing(FakeObsClient fake, CollectingSink sink)
	{
		var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
		var connection = new ObsConnection(fake,
			"ws://localhost:4455",
			null,
			reconnectDelay: TimeSpan.FromMilliseconds(1),
			logger: logger,
			failureSummaryInterval: TimeSpan.FromMilliseconds(50));
		connection.Start();

		return connection;
	}

	private static ObsConnection Connected(FakeObsClient fake, CollectingSink sink)
	{
		fake.ConnectRaisesConnected = true;
		fake.Status = new ObsStatus { CurrentScene = "Live" };

		var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
		var connection = new ObsConnection(fake,
			"ws://localhost:4455",
			null,
			reconnectDelay: TimeSpan.FromMilliseconds(1),
			logger: logger,
			failureSummaryInterval: TimeSpan.FromHours(1));
		connection.Start();

		return connection;
	}

	private static async Task FailFrom(FakeObsClient fake, ObsConnection connection, int attempts)
	{
		await WaitForAsync(() => connection.Status == ObsConnectionStatus.Connected, TimeSpan.FromSeconds(5));

		var connectsBefore = fake.ConnectCount;
		fake.ConnectRaisesConnected = false;
		fake.ConnectRaisesDisconnected = true;
		fake.DisconnectReason = "connection refused";
		fake.IsConnected = false;
		fake.RaiseDisconnected("connection refused");

		await WaitForAsync(() => fake.ConnectCount >= connectsBefore + attempts, TimeSpan.FromSeconds(30));
	}

	private static List<LogEvent> Loud(CollectingSink sink)
		=> sink.Events.Where(logEvent => logEvent.Level >= LogEventLevel.Information).ToList();

	private static async Task WaitForAsync(Func<bool> condition)
		=> await WaitForAsync(condition, TimeSpan.FromSeconds(2));

	private static async Task WaitForAsync(Func<bool> condition, TimeSpan budget)
	{
		var stopwatch = Stopwatch.StartNew();
		while (!condition() && stopwatch.Elapsed < budget)
		{
			await Task.Delay(20);
		}
	}

	// The connection logs from its own lifetime task while the assertions read the sink, so every
	// reader gets a snapshot rather than the live list.
	private sealed class CollectingSink : ILogEventSink
	{
		private readonly Lock _sync = new();
		private readonly List<LogEvent> _events = [];

		public List<LogEvent> Events
		{
			get
			{
				lock (_sync)
				{
					return [.. _events];
				}
			}
		}

		public void Emit(LogEvent logEvent)
		{
			lock (_sync)
			{
				_events.Add(logEvent);
			}
		}
	}
}
