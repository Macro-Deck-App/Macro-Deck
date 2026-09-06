using System.Diagnostics;
using MacroDeck.Plugin.Serilog.Tests.UnitTests.Support;
using Microsoft.Extensions.Options;
using Serilog.Events;

// Deliberately not "MacroDeck.Plugin.Serilog.Tests.UnitTests" (matching the project/folder name, the
// way the Hosting test project mirrors its own): a namespace containing the literal segment "Serilog"
// would shadow the real Serilog package namespace for every unqualified "Serilog.X" reference written
// inside it, since name lookup finds "MacroDeck.Plugin.Serilog" (this project's own tree) before it
// ever reaches the global "Serilog" namespace. Rooting test namespaces at "MacroDeck.Plugin.Logging"
// instead - matching the production code's own namespace, see MacroDeckLogSink.cs - avoids the clash
// entirely.
namespace MacroDeck.Plugin.Serilog.Tests.UnitTests;

/// <summary>
/// <see cref="MacroDeckLogSink" />'s queueing contract: priority dropping without reordering, and a
/// caller that is never blocked. These scenarios come from the requirement (issue #414's structured
/// plugin logging), not from reading the sink's implementation - see this repository's CLAUDE.md on
/// deriving tests from the requirement.
/// </summary>
[TestFixture]
public class MacroDeckLogSinkTests
{
	[Test]
	public void Information_events_are_dropped_before_warnings_and_errors_under_pressure()
	{
		var sink = new MacroDeckLogSink(Options.Create(new MacroDeckLoggingOptions()));

		const int total = 10_000;
		const int errorCount = 25;

		var random = new Random(20260811);
		var errorPositions = new HashSet<int>();
		while (errorPositions.Count < errorCount)
		{
			errorPositions.Add(random.Next(0, total));
		}

		var baseTime = DateTimeOffset.UtcNow;

		for (var i = 0; i < total; i++)
		{
			var level = errorPositions.Contains(i) ? LogEventLevel.Error : LogEventLevel.Information;
			sink.Emit(LogEventFactory.Create(baseTime.AddTicks(i), level, $"event-{i}"));
		}

		var drained = sink.Drain(total + errorCount);

		var deliveredErrors = drained.Events.Count(e => e.Level == LogEventLevel.Error);

		Assert.Multiple(() =>
		{
			// Every error survives the flood, however it was interleaved with the informational lines.
			Assert.That(deliveredErrors, Is.EqualTo(errorCount));

			// And the flood itself did not all survive - a queue that drops nothing would also pass the
			// assertion above while failing to demonstrate any dropping happened at all.
			Assert.That(drained.Events.Count, Is.LessThan(total + errorCount));
		});
	}

	[Test]
	public void A_disconnected_plugin_keeps_a_bounded_queue_and_never_blocks_the_caller()
	{
		var sink = new MacroDeckLogSink(Options.Create(new MacroDeckLoggingOptions()));

		const int total = 100_000;
		var baseTime = DateTimeOffset.UtcNow;

		var stopwatch = Stopwatch.StartNew();
		for (var i = 0; i < total; i++)
		{
			sink.Emit(LogEventFactory.Create(baseTime.AddTicks(i), LogEventLevel.Information, $"event-{i}"));
		}

		stopwatch.Stop();

		// A blocking implementation queueing 100,000 events one at a time against a full channel would
		// take vastly longer than this - a generous bound, not a tight one, since the point is "did not
		// hang", not "is fast".
		Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(10)));

		var drained = sink.Drain(total);

		// Far below the 100,000 logged - the queue is bounded and dropped the excess rather than
		// growing to hold it.
		Assert.That(drained.Events.Count, Is.LessThan(total / 2));
	}

	[Test]
	public void A_drained_batch_stays_in_timestamp_order()
	{
		var sink = new MacroDeckLogSink(Options.Create(new MacroDeckLoggingOptions()));
		var baseTime = DateTimeOffset.UtcNow;

		for (var i = 0; i < 300; i++)
		{
			var level = i % 7 == 0 ? LogEventLevel.Error : LogEventLevel.Information;
			sink.Emit(LogEventFactory.Create(baseTime.AddMilliseconds(i), level, $"event-{i}"));
		}

		var drained = sink.Drain(300);

		Assert.That(drained.Events.Select(e => e.Timestamp), Is.Ordered.Ascending);
	}
}
