using MacroDeck.Plugin.Protocol.Logging;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Tests.UnitTests.Auth;
using Serilog;
using Serilog.Events;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginLogRateLimiterTests
{
	[Test]
	public void A_burst_up_to_capacity_is_admitted_then_the_excess_is_refused_until_the_clock_advances()
	{
		var time = new ManualTimeProvider();
		var limiter = new PluginLogRateLimiter(time);
		const string pluginId = "com.example.plugin";

		var admittedInBurst = 0;
		for (var i = 0; i < 500; i++)
		{
			if (limiter.TryAcquire(pluginId))
			{
				admittedInBurst++;
			}
		}

		Assert.That(admittedInBurst, Is.EqualTo(500), "the full burst capacity should be admitted in one instant");

		var refusedAfterBurst = 0;
		for (var i = 0; i < 200; i++)
		{
			if (!limiter.TryAcquire(pluginId))
			{
				refusedAfterBurst++;
			}
		}

		Assert.That(refusedAfterBurst,
			Is.EqualTo(200),
			"the bucket is empty, so every further call in the same instant must be refused");

		time.Advance(TimeSpan.FromSeconds(1));

		var admittedAfterOneSecond = 0;
		for (var i = 0; i < 20; i++)
		{
			if (limiter.TryAcquire(pluginId))
			{
				admittedAfterOneSecond++;
			}
		}

		Assert.That(admittedAfterOneSecond, Is.EqualTo(20), "one second at the 20/s refill rate should admit 20 more");
	}

	[Test]
	public void An_idle_plugin_does_not_bank_more_than_one_burst()
	{
		var time = new ManualTimeProvider();
		var limiter = new PluginLogRateLimiter(time);
		const string pluginId = "com.example.plugin";

		time.Advance(TimeSpan.FromHours(1));

		var admitted = 0;
		for (var i = 0; i < 2000; i++)
		{
			if (limiter.TryAcquire(pluginId))
			{
				admitted++;
			}
		}

		Assert.That(admitted, Is.EqualTo(500), "an idle hour must still leave exactly one burst's worth");
	}

	[Test]
	public void Concurrent_debits_never_admit_more_than_the_budget()
	{
		var time = new ManualTimeProvider();
		var limiter = new PluginLogRateLimiter(time);
		const string pluginId = "com.example.plugin";

		// Two drain tasks can overlap across a reconnect, so the bucket is debited from more than one
		// thread. Without the lock this over-admits: read-modify-write on the token count races.
		var admitted = 0;
		Parallel.For(0,
			4000,
			_ =>
			{
				if (limiter.TryAcquire(pluginId))
				{
					Interlocked.Increment(ref admitted);
				}
			});

		Assert.That(admitted, Is.EqualTo(500), "the burst budget must hold under concurrent debits");
	}

	[Test]
	public void A_second_session_for_the_same_plugin_inherits_the_exhausted_budget()
	{
		// Driven through the ingestor rather than the limiter directly, because the session id only
		// exists at that level - a limiter keyed by session instead of plugin would pass any assertion
		// made against TryAcquire(pluginId) alone, which cannot even express the bypass.
		var time = new ManualTimeProvider();
		var registry = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		var sink = new CapturingRateLimitSink();
		var logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
		var ingestor = new PluginLogIngestor(new PluginLogRateLimiter(time),
			registry,
			new FakePluginSupervisor(),
			time,
			() => logger);

		const string pluginId = "com.example.plugin";

		for (var batch = 0; batch < 9; batch++)
		{
			ingestor.Ingest(pluginId, "session-one", Batch(64));
		}

		var admittedUnderFirstSession = sink.Events.Count;

		sink.Events.Clear();
		ingestor.Ingest(pluginId, "session-two", Batch(64));

		Assert.Multiple(() =>
		{
			Assert.That(admittedUnderFirstSession, Is.EqualTo(500), "the burst bounds the first session");
			Assert.That(sink.Events,
				Is.Empty,
				"a reconnect must not buy a flooding plugin a fresh budget - keying the bucket by session " +
				"instead of plugin id would reopen exactly this bypass");
		});
	}

	private static List<LogEventDto> Batch(int count)
		=> Enumerable.Range(0, count)
			.Select(i => new LogEventDto
			{
				Timestamp = DateTimeOffset.UtcNow,
				Level = LogLevels.Information,
				MessageTemplate = "event",
				RenderedMessage = $"event {i}"
			})
			.ToList();

	private sealed class CapturingRateLimitSink : Serilog.Core.ILogEventSink
	{
		public List<LogEvent> Events { get; } = [];

		public void Emit(LogEvent logEvent) => Events.Add(logEvent);
	}

	[Test]
	public void Buckets_are_independent_per_plugin_id()
	{
		var time = new ManualTimeProvider();
		var limiter = new PluginLogRateLimiter(time);

		for (var i = 0; i < 500; i++)
		{
			Assert.That(limiter.TryAcquire("com.example.a"), Is.True);
		}

		Assert.That(limiter.TryAcquire("com.example.a"), Is.False);
		Assert.That(limiter.TryAcquire("com.example.b"),
			Is.True,
			"a different plugin id must have its own, still-full bucket");
	}
}
