using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Domain.Enums;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarWidgetAutomationHarness;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarWidgetTriggerSchedulerTests
{
	private const string StartsSoon = "onCalendarEventStartsSoon";
	private const string Started = "onCalendarEventStarted";
	private const string Ended = "onCalendarEventEnded";

	private CalendarWidgetAutomationHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new CalendarWidgetAutomationHarness();

	[TearDown]
	public async Task TearDown() => await _harness.DisposeAsync();

	[Test]
	public async Task Starts_soon_runs_the_widgets_flow_at_its_own_lead_time_once_with_the_event_as_payload()
	{
		_harness.With(Event("review", Noon.AddMinutes(30), TimeSpan.FromMinutes(30), title: "Review"));
		var widget = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutNextEvent, Data(new { leadTime = 10 }, StartsSoon));

		await _harness.RunAtAsync(Noon.AddMinutes(19));
		var early = _harness.Executor.Requests.Count;
		await _harness.RunAtAsync(Noon.AddMinutes(20));
		await _harness.RunAtAsync(Noon.AddMinutes(21));

		var request = _harness.Executor.Requests.Single();
		Assert.Multiple(() =>
		{
			Assert.That(early, Is.Zero);
			Assert.That(request.Trigger, Is.EqualTo(TriggerSelector.ByType(StartsSoon)));
			Assert.That(request.OwnerWidgetId, Is.EqualTo(widget.Id));
			Assert.That(request.Scope, Is.EqualTo(VariableScope.Widget));
			Assert.That(request.ScopeRefId, Is.EqualTo(widget.Id.ToString()));
			Assert.That(request.Origin, Is.EqualTo(ExecutionOrigin.Host));
			Assert.That(request.EventParameters!["title"], Is.EqualTo("Review"));
			Assert.That(request.EventParameters!["eventId"], Is.EqualTo("review"));
			Assert.That(request.EventParameters!["provider"], Is.EqualTo("Google Calendar"));
		});
	}

	[Test]
	public async Task Only_events_of_the_widgets_calendars_and_all_day_setting_run_its_flows()
	{
		_harness.With(Event("team", Noon.AddMinutes(10), TimeSpan.FromMinutes(30), "team"))
			.With(Event("main", Noon.AddMinutes(10), TimeSpan.FromMinutes(30)))
			.With(Event("holiday", Noon.AddHours(12), TimeSpan.FromDays(1), "team") with { IsAllDay = true });
		_harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutAgenda,
			Data(new { calendars = new[] { TeamCalendar }, showAllDay = false }, Started));

		foreach (var instant in new[] { Noon, Noon.AddMinutes(10), Noon.AddHours(12) })
		{
			await _harness.RunAtAsync(instant);
		}

		Assert.That(_harness.Executor.Requests.Select(r => r.EventParameters!["eventId"]), Is.EqualTo(new[] { "team" }));
	}

	[Test]
	public async Task Started_and_ended_run_as_the_event_passes_but_an_event_over_before_the_widget_existed_never_replays()
	{
		_harness.With(Event("morning", Noon.AddMinutes(10), TimeSpan.FromMinutes(20)))
			.With(Event("afternoon", Noon.AddMinutes(70), TimeSpan.FromMinutes(10)));
		await _harness.RunAtAsync(Noon);
		await _harness.RunAtAsync(Noon.AddMinutes(60));

		_harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutNextEvent, Data(null, Started, Ended));
		foreach (var minute in new[] { 61, 70, 70, 80, 81 })
		{
			await _harness.RunAtAsync(Noon.AddMinutes(minute));
		}

		Assert.That(_harness.Executor.Requests.Select(r => (r.Trigger.Value, r.EventParameters!["eventId"])),
			Is.EqualTo(new (string, object?)[] { (Started, "afternoon"), (Ended, "afternoon") }));
	}

	[Test]
	public async Task An_event_first_seen_inside_the_lead_time_still_gets_its_warning_once()
	{
		_harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutNextEvent, Data(new { leadTime = 15 }, StartsSoon));
		await _harness.RunAtAsync(Noon);

		_harness.With(Event("moved", Noon.AddMinutes(10), TimeSpan.FromMinutes(30)));
		await _harness.RunAtAsync(Noon.AddMinutes(5));
		await _harness.RunAtAsync(Noon.AddMinutes(6));

		Assert.That(_harness.Executor.Requests, Has.Count.EqualTo(1));
	}

	[Test]
	public async Task A_widget_without_a_flow_for_a_calendar_trigger_runs_nothing()
	{
		_harness.With(Event("review", Noon.AddMinutes(10), TimeSpan.FromMinutes(20)));
		_harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutNextEvent,
			"""{"flows":[{"triggerType":"onCalendarEventStarted","children":[{"type":"action","disabled":true}]}]}""");

		await _harness.RunAtAsync(Noon);
		await _harness.RunAtAsync(Noon.AddMinutes(10));

		Assert.That(_harness.Executor.Requests, Is.Empty);
	}

	[Test]
	public async Task A_new_widget_and_a_changed_lead_time_are_planned_right_away()
	{
		_harness.With(Event("review", Noon.AddMinutes(30), TimeSpan.FromMinutes(30)));
		await _harness.Cache.SyncAsync(CancellationToken.None);
		using var service = _harness.BackgroundService();
		await service.StartAsync(CancellationToken.None);
		await SleepingUntilAsync(Noon.AddHours(1));

		var widget = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutNextEvent, Data(new { leadTime = 5 }, StartsSoon));
		await _harness.Lifecycle.ChangedAsync([widget]);
		await SleepingUntilAsync(Noon.AddMinutes(1));
		var beforeChange = _harness.Executor.Requests.Count;

		widget.Data = Data(new { leadTime = 45 }, StartsSoon);
		await _harness.Lifecycle.ChangedAsync([widget]);
		await EventuallyAsync(() => _harness.Executor.Requests.Count == 1);
		await service.StopAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(beforeChange, Is.Zero);
			Assert.That(_harness.Time.Now, Is.EqualTo(Noon), "nothing but the change itself may have woken the planner");
		});
	}

	private async Task SleepingUntilAsync(DateTimeOffset instant)
		=> await EventuallyAsync(() => _harness.Time.ActiveDueTimes.Contains(instant));

	private static async Task EventuallyAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);

		while (!condition())
		{
			Assert.That(DateTime.UtcNow, Is.LessThan(deadline), "The condition was not met in time.");
			await Task.Delay(10);
		}
	}
}
