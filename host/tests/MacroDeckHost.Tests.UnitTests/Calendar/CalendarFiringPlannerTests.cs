using MacroDeckHost.Application.Calendar;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarFiringPlannerTests
{
	private static readonly CalendarFiringTarget _started = new(CalendarFiringKind.Started, "started");
	private static readonly CalendarFiringTarget _ended = new(CalendarFiringKind.Ended, "ended");

	private CalendarFiringPlanner _planner = null!;

	[SetUp]
	public void SetUp() => _planner = new CalendarFiringPlanner();

	[Test]
	public void An_instant_fires_once_when_it_passes_and_the_next_one_is_named_until_then()
	{
		var review = Summary("review", Noon.AddMinutes(10), TimeSpan.FromMinutes(20));

		var before = Plan(Noon, [review], _started, _ended);
		var atStart = Plan(Noon.AddMinutes(10), [review], _started, _ended);
		var replanned = Plan(Noon.AddMinutes(11), [review], _started, _ended);

		Assert.Multiple(() =>
		{
			Assert.That(before.Due, Is.Empty);
			Assert.That(before.Next, Is.EqualTo(Noon.AddMinutes(10)));
			Assert.That(atStart.Due.Select(f => f.Target.Kind), Is.EqualTo(new[] { CalendarFiringKind.Started }));
			Assert.That(atStart.Next, Is.EqualTo(Noon.AddMinutes(30)));
			Assert.That(replanned.Due, Is.Empty);
		});
	}

	[Test]
	public void Instants_that_passed_before_planning_began_are_never_replayed()
	{
		var running = Summary("running", Noon.AddMinutes(-2), TimeSpan.FromMinutes(30));

		var first = Plan(Noon, [running], _started);

		Assert.That(first.Due, Is.Empty);
	}

	[Test]
	public void A_passed_instant_first_seen_within_one_poll_still_fires_and_an_older_one_does_not()
	{
		Plan(Noon, [], _started);
		var fresh = Summary("fresh", Noon.AddMinutes(3), TimeSpan.FromHours(1));
		var stale = Summary("stale", Noon.AddMinutes(1), TimeSpan.FromHours(1));

		var due = Plan(Noon.AddMinutes(9), [fresh, stale], _started);

		Assert.That(due.Due.Select(f => f.Event.EventId), Is.EqualTo(new[] { "fresh" }));
	}

	[Test]
	public void Starts_soon_fires_at_the_lead_time_or_when_first_seen_inside_it_but_never_after_the_start()
	{
		var target = new CalendarFiringTarget(CalendarFiringKind.StartsSoon, "soon", TimeSpan.FromMinutes(15));
		var inside = Summary("inside", Noon.AddMinutes(5), TimeSpan.FromMinutes(30));
		var started = Summary("started", Noon.AddMinutes(-5), TimeSpan.FromMinutes(30));
		var later = Summary("later", Noon.AddMinutes(40), TimeSpan.FromMinutes(30));

		var plan = Plan(Noon, [inside, started, later], target);

		Assert.Multiple(() =>
		{
			Assert.That(plan.Due.Select(f => f.Event.EventId), Is.EqualTo(new[] { "inside" }));
			Assert.That(plan.Due.Single().Instant, Is.EqualTo(Noon.AddMinutes(-10)));
			Assert.That(plan.Next, Is.EqualTo(Noon.AddMinutes(25)));
		});
	}

	[Test]
	public void A_target_only_sees_the_events_it_accepts_and_each_target_fires_on_its_own()
	{
		var mine = Summary("mine", Noon.AddMinutes(5), TimeSpan.FromMinutes(30));
		var other = Summary("other", Noon.AddMinutes(5), TimeSpan.FromMinutes(30));
		var first = new CalendarFiringTarget(CalendarFiringKind.Started, "first", Accepts: e => e.EventId == "mine");
		var second = new CalendarFiringTarget(CalendarFiringKind.Started, "second");

		Plan(Noon, [mine, other], first, second);
		var due = Plan(Noon.AddMinutes(5), [mine, other], first, second);

		Assert.That(due.Due.Select(f => (f.Target.Key, f.Event.EventId)),
			Is.EquivalentTo(new (object, string)[] { ("first", "mine"), ("second", "mine"), ("second", "other") }));
	}

	private CalendarFiringPlan Plan(DateTimeOffset now, CalendarEventSummary[] events, params CalendarFiringTarget[] targets)
		=> _planner.Plan(new CalendarSnapshot(Noon.AddDays(-1), Noon.AddDays(8), [], events), now, targets);

	private static CalendarEventSummary Summary(string id, DateTimeOffset start, TimeSpan duration)
		=> new()
		{
			InstanceKey = "main/" + id,
			EventId = id,
			AccountId = "app.google::work",
			AccountName = "work@example.com",
			ProviderName = "Google Calendar",
			IntegrationId = "app.google",
			CalendarKey = "main",
			CalendarId = "main",
			CalendarName = "Main",
			Title = id,
			Start = start,
			End = start + duration,
		};
}
