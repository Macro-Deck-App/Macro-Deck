using System.Globalization;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Events;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarWidgetAutomationHarness;
using Writer = MacroDeckHost.Application.Calendar.CalendarWidgetVariableWriter;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarWidgetVariableTests
{
	private CalendarWidgetAutomationHarness _harness = null!;

	[SetUp]
	public async Task SetUp()
	{
		_harness = new CalendarWidgetAutomationHarness()
			.With(Event("review", Noon.AddMinutes(30), TimeSpan.FromMinutes(30), "team", "Review",
				"https://meet.example.com/review") with { Location = "Room 4" });
		await _harness.Cache.SyncAsync(CancellationToken.None);
	}

	[TearDown]
	public async Task TearDown() => await _harness.DisposeAsync();

	[Test]
	public async Task A_placed_widget_publishes_the_event_it_shows()
	{
		var widget = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutNextEvent, "{}");

		await _harness.Lifecycle.ChangedAsync([widget]);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Variable(widget, Writer.Title), Is.EqualTo("Review"));
			Assert.That(DateTimeOffset.Parse(_harness.Variable(widget, Writer.Start)!, CultureInfo.InvariantCulture),
				Is.EqualTo(Noon.AddMinutes(30)));
			Assert.That(DateTimeOffset.Parse(_harness.Variable(widget, Writer.End)!, CultureInfo.InvariantCulture),
				Is.EqualTo(Noon.AddMinutes(60)));
			Assert.That(_harness.Variable(widget, Writer.Countdown), Is.EqualTo("in 30 minutes"));
			Assert.That(_harness.Variable(widget, Writer.Minutes), Is.EqualTo("30"));
			Assert.That(_harness.Variable(widget, Writer.Running)?.ToLowerInvariant(), Is.EqualTo("false"));
			Assert.That(_harness.Variable(widget, Writer.Location), Is.EqualTo("Room 4"));
			Assert.That(_harness.Variable(widget, Writer.MeetingUrl), Is.EqualTo("https://meet.example.com/review"));
			Assert.That(_harness.Variable(widget, Writer.Calendar), Is.EqualTo("Calendar team"));
		});
	}

	[Test]
	public async Task Each_minute_updates_only_the_countdown_and_the_minutes_and_a_running_event_reads_zero()
	{
		var widget = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutNextEvent, "{}");
		var next = await RefreshAtAsync(Noon);
		_harness.Mediator.Published.Clear();

		await RefreshAtAsync(next);
		var changed = _harness.Mediator.Published.OfType<VariableValueChangedNotification>()
			.Select(notification => notification.Variable.Name)
			.ToList();
		await RefreshAtAsync(Noon.AddMinutes(31));

		Assert.Multiple(() =>
		{
			Assert.That(next, Is.EqualTo(Noon.AddMinutes(1)));
			Assert.That(changed, Is.EquivalentTo(new[] { Writer.Countdown, Writer.Minutes }));
			Assert.That(_harness.Variable(widget, Writer.Minutes), Is.EqualTo("0"));
			Assert.That(_harness.Variable(widget, Writer.Running)?.ToLowerInvariant(), Is.EqualTo("true"));
			Assert.That(_harness.Variable(widget, Writer.Countdown), Is.EqualTo("Now"));
		});
	}

	[Test]
	public async Task Without_an_event_to_show_the_variables_are_removed()
	{
		var widget = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutAgenda, "{}");
		await RefreshAtAsync(Noon);

		await RefreshAtAsync(Noon.AddMinutes(61));

		Assert.That(_harness.VariableNames(widget), Is.Empty);
	}

	[Test]
	public async Task An_agenda_publishes_its_first_event_within_its_calendars_and_days()
	{
		_harness.With(Event("tomorrow", Noon.AddDays(1), TimeSpan.FromHours(1), "main", "Tomorrow"));
		await _harness.Cache.SyncAsync(CancellationToken.None);
		var today = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutAgenda,
			Data(new { calendars = new[] { MainCalendar }, days = 1 }));
		var twoDays = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutAgenda,
			Data(new { calendars = new[] { MainCalendar }, days = 2 }));

		await RefreshAtAsync(Noon);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.VariableNames(today), Is.Empty);
			Assert.That(_harness.Variable(twoDays, Writer.Title), Is.EqualTo("Tomorrow"));
		});
	}

	[Test]
	public async Task The_published_event_follows_the_layout_the_widget_is_switched_to()
	{
		_harness.With(Event("tomorrow", Noon.AddDays(1), TimeSpan.FromHours(1), "main", "Tomorrow"));
		var settings = new { days = 1, whenStarted = CalendarWidgetTypes.WhenStartedNext };
		var widget = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutAgenda, Data(settings));
		await RefreshAtAsync(Noon.AddMinutes(40));
		var asAgenda = _harness.Variable(widget, Writer.Title);

		widget.Data = CalendarWidgetHarness.WithLayout(CalendarWidgetTypes.LayoutNextEvent, Data(settings));
		await _harness.Lifecycle.ChangedAsync([widget]);

		Assert.Multiple(() =>
		{
			Assert.That(asAgenda, Is.EqualTo("Review"), "the agenda lists the running event first");
			Assert.That(_harness.Variable(widget, Writer.Title), Is.EqualTo("Tomorrow"),
				"the next event layout set to show the next event skips the running one");
		});
	}

	[Test]
	public async Task A_widget_deleted_while_its_variables_are_written_is_left_without_any()
	{
		var widget = _harness.Folders.AddCalendar(CalendarWidgetTypes.LayoutNextEvent, "{}");
		_harness.Mediator.BeforePublish = _ => _harness.Folders.Remove(widget);

		await _harness.Lifecycle.ChangedAsync([widget]);

		Assert.That(_harness.VariableNames(widget), Is.Empty);
	}

	private async Task<DateTimeOffset> RefreshAtAsync(DateTimeOffset now)
	{
		_harness.Time.Now = now;
		await _harness.Cache.SyncAsync(CancellationToken.None);
		return await _harness.Variables.RefreshAllAsync();
	}
}
