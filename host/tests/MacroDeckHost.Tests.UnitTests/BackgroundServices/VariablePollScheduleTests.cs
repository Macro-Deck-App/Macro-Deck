using MacroDeckHost.Infrastructure.BackgroundServices;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
public class VariablePollScheduleTests
{
	private static readonly DateTime _start = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

	[Test]
	public void Is_due_on_the_first_tick()
	{
		var schedule = new VariablePollSchedule(TimeSpan.FromSeconds(1));

		Assert.That(schedule.IsDue(_start), Is.True);
	}

	[Test]
	public void Not_due_again_before_the_interval_elapses()
	{
		var schedule = new VariablePollSchedule(TimeSpan.FromSeconds(1));
		schedule.IsDue(_start);

		Assert.That(schedule.IsDue(_start + TimeSpan.FromMilliseconds(750)), Is.False);
	}

	[Test]
	public void Due_again_once_the_interval_elapses()
	{
		var schedule = new VariablePollSchedule(TimeSpan.FromSeconds(1));
		schedule.IsDue(_start);

		Assert.That(schedule.IsDue(_start + TimeSpan.FromSeconds(1)), Is.True);
	}

	// Regression: a one-second variable polled by a faster tick must fire once per second,
	// never stretching to two seconds because a tick landed a hair early against the interval.
	[Test]
	public void Keeps_a_one_second_cadence_when_ticked_every_250ms()
	{
		var interval = TimeSpan.FromSeconds(1);
		var tick = TimeSpan.FromMilliseconds(250);
		var schedule = new VariablePollSchedule(interval);

		var pollTimes = new List<DateTime>();
		var jitter = new[] { 0, 3, -2, 5, -4, 1, -3, 2 };
		for (var i = 0; i < 40; i++)
		{
			var now = _start + tick * i + TimeSpan.FromMilliseconds(jitter[i % jitter.Length]);
			if (schedule.IsDue(now))
			{
				pollTimes.Add(now);
			}
		}

		Assert.That(pollTimes, Has.Count.EqualTo(10), "one poll per second over the 10s window");
		for (var i = 1; i < pollTimes.Count; i++)
		{
			var gap = pollTimes[i] - pollTimes[i - 1];
			Assert.That(gap,
				Is.LessThan(TimeSpan.FromMilliseconds(1500)),
				$"gap {i} was {gap.TotalMilliseconds}ms - a poll was skipped");
		}
	}

	[Test]
	public void Does_not_drift_over_many_intervals()
	{
		var interval = TimeSpan.FromSeconds(1);
		var tick = TimeSpan.FromMilliseconds(250);
		var schedule = new VariablePollSchedule(interval);

		var polls = 0;
		for (var i = 0; i < 120; i++)
		{
			if (schedule.IsDue(_start + tick * i))
			{
				polls++;
			}
		}

		Assert.That(polls, Is.EqualTo(30));
	}

	[Test]
	public void Resyncs_after_a_long_stall_without_a_catch_up_burst()
	{
		var interval = TimeSpan.FromSeconds(1);
		var schedule = new VariablePollSchedule(interval);
		schedule.IsDue(_start);

		var afterStall = _start + TimeSpan.FromSeconds(10);
		Assert.That(schedule.IsDue(afterStall), Is.True, "one poll right after the stall");
		Assert.That(schedule.IsDue(afterStall + TimeSpan.FromMilliseconds(250)),
			Is.False,
			"no immediate second poll - the missed intervals are dropped, not replayed");
		Assert.That(schedule.IsDue(afterStall + interval),
			Is.True,
			"cadence resumes one interval after the stall");
	}
}
