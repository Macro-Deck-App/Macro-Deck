using MacroDeck.Sdk.Logging;

namespace MacroDeck.Sdk.Tests.UnitTests.Logging;

[TestFixture]
public class FailureEpisodeTrackerTests
{
	[Test]
	public void The_first_failure_is_the_onset()
	{
		var time = new ManualTimeProvider();
		var tracker = new FailureEpisodeTracker(time: time);

		var signal = tracker.RecordFailure("boom");

		Assert.Multiple(() =>
		{
			Assert.That(signal.Kind, Is.EqualTo(FailureEpisodeSignalKind.Onset));
			Assert.That(signal.ConsecutiveFailures, Is.EqualTo(1));
			Assert.That(signal.StartedAt, Is.EqualTo(time.Now));
			Assert.That(signal.LastError, Is.EqualTo("boom"));
			Assert.That(tracker.IsActive, Is.True);
		});
	}

	[Test]
	public void Failures_between_summaries_are_quiet()
	{
		var time = new ManualTimeProvider();
		var tracker = new FailureEpisodeTracker(TimeSpan.FromMinutes(5), time);
		tracker.RecordFailure("boom");

		time.Advance(TimeSpan.FromMinutes(4));
		var signal = tracker.RecordFailure("still boom");

		Assert.Multiple(() =>
		{
			Assert.That(signal.Kind, Is.EqualTo(FailureEpisodeSignalKind.Quiet));
			Assert.That(signal.ConsecutiveFailures, Is.EqualTo(2));
		});
	}

	[Test]
	public void A_summary_is_due_once_per_interval()
	{
		var time = new ManualTimeProvider();
		var tracker = new FailureEpisodeTracker(TimeSpan.FromMinutes(5), time);
		var startedAt = time.Now;
		tracker.RecordFailure("boom");

		time.Advance(TimeSpan.FromMinutes(5));
		var summary = tracker.RecordFailure("still boom");
		var rightAfter = tracker.RecordFailure("still boom");
		time.Advance(TimeSpan.FromMinutes(5));
		var nextSummary = tracker.RecordFailure("still boom");

		Assert.Multiple(() =>
		{
			Assert.That(summary.Kind, Is.EqualTo(FailureEpisodeSignalKind.SummaryDue));
			Assert.That(summary.StartedAt, Is.EqualTo(startedAt));
			Assert.That(summary.Duration, Is.EqualTo(TimeSpan.FromMinutes(5)));
			Assert.That(summary.ConsecutiveFailures, Is.EqualTo(2));
			Assert.That(rightAfter.Kind, Is.EqualTo(FailureEpisodeSignalKind.Quiet));
			Assert.That(nextSummary.Kind, Is.EqualTo(FailureEpisodeSignalKind.SummaryDue));
			Assert.That(nextSummary.Duration, Is.EqualTo(TimeSpan.FromMinutes(10)));
		});
	}

	[Test]
	public void Success_closes_the_episode_and_reports_its_extent()
	{
		var time = new ManualTimeProvider();
		var tracker = new FailureEpisodeTracker(time: time);
		var startedAt = time.Now;
		tracker.RecordFailure("boom");
		tracker.RecordFailure("boom");
		time.Advance(TimeSpan.FromMinutes(2));

		var end = tracker.RecordSuccess();

		Assert.Multiple(() =>
		{
			Assert.That(end, Is.Not.Null);
			Assert.That(end!.StartedAt, Is.EqualTo(startedAt));
			Assert.That(end.Duration, Is.EqualTo(TimeSpan.FromMinutes(2)));
			Assert.That(end.Failures, Is.EqualTo(2));
			Assert.That(tracker.IsActive, Is.False);
			Assert.That(tracker.StartedAt, Is.Null);
		});
	}

	[Test]
	public void Success_without_an_episode_reports_nothing()
	{
		var tracker = new FailureEpisodeTracker();

		Assert.That(tracker.RecordSuccess(), Is.Null);
	}

	[Test]
	public void The_next_episode_starts_fresh()
	{
		var time = new ManualTimeProvider();
		var tracker = new FailureEpisodeTracker(TimeSpan.FromMinutes(5), time);
		tracker.RecordFailure("boom");
		time.Advance(TimeSpan.FromMinutes(6));
		tracker.RecordSuccess();

		time.Advance(TimeSpan.FromMinutes(1));
		var signal = tracker.RecordFailure("boom again");

		Assert.Multiple(() =>
		{
			Assert.That(signal.Kind, Is.EqualTo(FailureEpisodeSignalKind.Onset));
			Assert.That(signal.ConsecutiveFailures, Is.EqualTo(1));
			Assert.That(signal.StartedAt, Is.EqualTo(time.Now));
		});
	}

	private sealed class ManualTimeProvider : TimeProvider
	{
		public DateTimeOffset Now { get; private set; } = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

		public override DateTimeOffset GetUtcNow() => Now;

		public void Advance(TimeSpan delta) => Now += delta;
	}
}
