using System.Text.Json;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Infrastructure.Triggers;

namespace MacroDeckHost.Tests.UnitTests.Triggers;

[TestFixture]
public class TriggerScheduleTests
{
	private static readonly TimeZoneInfo _berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

	private static EventSubscription Subscription(string eventId, params (string Name, string Json)[] configuration)
	{
		var values = configuration.ToDictionary(c => c.Name,
			c => new EventConfigurationValue(JsonDocument.Parse(c.Json).RootElement.Clone()),
			StringComparer.Ordinal);

		return new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			$"time::{eventId}",
			values,
			null);
	}

	private static DateTimeOffset? Next(EventSubscription subscription, DateTimeOffset after)
		=> TriggerSchedule.TryCreate(subscription)?.NextOccurrence(after, _berlin);

	[Test]
	public void An_interval_schedules_from_the_given_moment()
	{
		var subscription = Subscription(TimeEventProvider.IntervalEventId, ("every", "30"), ("unit", "\"seconds\""));
		var after = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

		Assert.That(Next(subscription, after), Is.EqualTo(after.AddSeconds(30)));
	}

	[Test]
	public void Interval_units_are_honoured()
	{
		var after = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

		Assert.Multiple(() =>
		{
			Assert.That(Next(Subscription(TimeEventProvider.IntervalEventId, ("every", "2"), ("unit", "\"minutes\"")),
					after),
				Is.EqualTo(after.AddMinutes(2)));
			Assert.That(Next(Subscription(TimeEventProvider.IntervalEventId, ("every", "3"), ("unit", "\"hours\"")),
					after),
				Is.EqualTo(after.AddHours(3)));
		});
	}

	[Test]
	public void An_unset_unit_defaults_to_minutes()
	{
		var subscription = Subscription(TimeEventProvider.IntervalEventId, ("every", "5"));
		var after = new DateTimeOffset(2026, 3, 1, 12, 0, 0, TimeSpan.Zero);

		Assert.That(Next(subscription, after), Is.EqualTo(after.AddMinutes(5)));
	}

	[Test]
	public void A_sub_second_interval_is_refused()
	{
		var subscription = Subscription(TimeEventProvider.IntervalEventId, ("every", "0"), ("unit", "\"seconds\""));

		Assert.That(TriggerSchedule.TryCreate(subscription), Is.Null);
	}

	[Test]
	public void A_daily_schedule_fires_at_the_configured_local_time()
	{
		var subscription = Subscription(TimeEventProvider.DailyEventId, ("time", "\"09:30\""));
		var after = new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.FromHours(1));

		var next = Next(subscription, after);

		Assert.That(TimeZoneInfo.ConvertTime(next!.Value, _berlin).TimeOfDay, Is.EqualTo(new TimeSpan(9, 30, 0)));
	}

	[Test]
	public void A_daily_schedule_keeps_its_local_time_across_a_dst_transition()
	{
		var subscription = Subscription(TimeEventProvider.DailyEventId, ("time", "\"09:30\""));

		var beforeTransition = new DateTimeOffset(2026, 3, 28, 10, 0, 0, TimeSpan.FromHours(1));
		var next = Next(subscription, beforeTransition);

		Assert.Multiple(() =>
		{
			Assert.That(TimeZoneInfo.ConvertTime(next!.Value, _berlin).TimeOfDay, Is.EqualTo(new TimeSpan(9, 30, 0)));
			Assert.That(next!.Value.Offset, Is.EqualTo(TimeSpan.FromHours(2)));
		});
	}

	[Test]
	public void A_weekly_schedule_fires_on_the_configured_day()
	{
		var subscription = Subscription(TimeEventProvider.WeeklyEventId,
			("dayOfWeek", "\"MON\""),
			("time", "\"09:00\""));
		var after = new DateTimeOffset(2026, 3, 4, 0, 0, 0, TimeSpan.FromHours(1)); // Wednesday

		var next = Next(subscription, after);

		Assert.That(TimeZoneInfo.ConvertTime(next!.Value, _berlin).DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
	}

	[Test]
	public void A_monthly_schedule_fires_on_the_configured_day()
	{
		var subscription = Subscription(TimeEventProvider.MonthlyEventId,
			("dayOfMonth", "15"),
			("time", "\"09:00\""));
		var after = new DateTimeOffset(2026, 3, 20, 0, 0, 0, TimeSpan.FromHours(1));

		var next = Next(subscription, after);

		Assert.Multiple(() =>
		{
			Assert.That(TimeZoneInfo.ConvertTime(next!.Value, _berlin).Day, Is.EqualTo(15));
			Assert.That(TimeZoneInfo.ConvertTime(next!.Value, _berlin).Month, Is.EqualTo(4));
		});
	}

	[Test]
	public void A_monthly_schedule_on_the_31st_skips_short_months()
	{
		var subscription = Subscription(TimeEventProvider.MonthlyEventId,
			("dayOfMonth", "31"),
			("time", "\"09:00\""));
		var afterJanuary = new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.FromHours(1));

		var next = Next(subscription, afterJanuary);

		Assert.That(TimeZoneInfo.ConvertTime(next!.Value, _berlin).Month, Is.EqualTo(3));
	}

	[Test]
	public void A_cron_expression_is_used_as_authored()
	{
		var subscription = Subscription(TimeEventProvider.CronEventId, ("expression", "\"0 9 * * MON-FRI\""));
		var saturday = new DateTimeOffset(2026, 3, 7, 12, 0, 0, TimeSpan.FromHours(1));

		var next = Next(subscription, saturday);

		Assert.That(TimeZoneInfo.ConvertTime(next!.Value, _berlin).DayOfWeek, Is.EqualTo(DayOfWeek.Monday));
	}

	[Test]
	public void An_invalid_cron_expression_yields_no_schedule()
	{
		var subscription = Subscription(TimeEventProvider.CronEventId, ("expression", "\"not a cron\""));

		Assert.That(TriggerSchedule.TryCreate(subscription), Is.Null);
	}

	[Test]
	public void An_incomplete_configuration_yields_no_schedule()
	{
		Assert.Multiple(() =>
		{
			Assert.That(TriggerSchedule.TryCreate(Subscription(TimeEventProvider.DailyEventId)), Is.Null);
			Assert.That(TriggerSchedule.TryCreate(Subscription(TimeEventProvider.DailyEventId, ("time", "\"25:00\""))),
				Is.Null);
			Assert.That(TriggerSchedule.TryCreate(Subscription(TimeEventProvider.MonthlyEventId,
					("dayOfMonth", "32"),
					("time", "\"09:00\""))),
				Is.Null);
			Assert.That(TriggerSchedule.TryCreate(Subscription(TimeEventProvider.IntervalEventId)), Is.Null);
		});
	}

	[Test]
	public void An_event_from_another_provider_is_not_a_schedule()
	{
		var subscription = new EventSubscription(EventTriggerOwner.ForWidget(Guid.NewGuid()),
			"t1",
			"obs::scene-changed",
			EventSubscription.NoConfiguration,
			null);

		Assert.That(TriggerSchedule.TryCreate(subscription), Is.Null);
	}
}
