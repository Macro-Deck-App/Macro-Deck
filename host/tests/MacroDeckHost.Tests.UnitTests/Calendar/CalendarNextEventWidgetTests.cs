using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Widgets.Calendar;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTree;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarNextEventWidgetTests
{
	private CalendarWidgetHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new CalendarWidgetHarness();

	[Test]
	public async Task The_countdown_names_hours_and_minutes_from_the_catalog()
	{
		_harness.With(Event("planning", Noon.AddHours(1).AddMinutes(43), TimeSpan.FromHours(1), title: "Planning"));
		await _harness.SyncAsync();

		await using var session = await Open();
		var expected = Strings.NextEvent.StartsIn(duration: Strings.Countdown.Pair(first: Strings.Countdown.Hours(1),
			second: Strings.Countdown.Minutes(43)));

		Assert.Multiple(() =>
		{
			Assert.That(Title(session), Is.EqualTo("Planning"));
			Assert.That(When(session), Is.EqualTo(Resolve(expected)));
			Assert.That(When(session, "de"), Is.EqualTo(Resolve(expected, "de")));
			Assert.That(When(session, "de"), Is.Not.EqualTo(When(session)), "the countdown follows the language");
		});
	}

	[Test]
	public async Task An_event_days_away_counts_in_the_nearest_whole_day_and_a_closer_one_keeps_its_hours()
	{
		_harness.With(Event("offsite", Noon.AddDays(5).AddHours(15), TimeSpan.FromHours(1), title: "Offsite"));
		await _harness.SyncAsync();
		await using var farAway = await Open();
		var far = When(farAway);

		_harness.With(Event("review", Noon.AddDays(1).AddHours(23), TimeSpan.FromHours(1), title: "Review"));
		await _harness.SyncAsync();
		await using var closer = await Open();

		Assert.Multiple(() =>
		{
			Assert.That(far, Is.EqualTo(Resolve(Strings.NextEvent.StartsIn(duration: Strings.Countdown.Days(6)))));
			Assert.That(When(closer), Is.EqualTo(Resolve(Strings.NextEvent.StartsIn(duration: Strings.Countdown.Pair(
				first: Strings.Countdown.Days(1),
				second: Strings.Countdown.Hours(23))))));
		});
	}

	[Test]
	public async Task The_countdown_ticks_down_on_its_own_to_under_a_minute_and_then_now()
	{
		_harness.With(Event("standup", Noon.AddMinutes(5), TimeSpan.FromMinutes(15), title: "Standup"));
		await _harness.SyncAsync();
		await using var session = await Open();

		var fiveMinutes = When(session);
		_harness.Time.Advance(TimeSpan.FromMinutes(1));
		var fourMinutes = When(session);
		_harness.Time.Advance(TimeSpan.FromMinutes(3).Add(TimeSpan.FromSeconds(30)));
		var underAMinute = When(session);
		_harness.Time.Advance(TimeSpan.FromSeconds(30));
		var started = When(session);

		Assert.Multiple(() =>
		{
			Assert.That(fiveMinutes, Is.EqualTo(Resolve(Strings.NextEvent.StartsIn(duration: Strings.Countdown.Minutes(5)))));
			Assert.That(fourMinutes, Is.EqualTo(Resolve(Strings.NextEvent.StartsIn(duration: Strings.Countdown.Minutes(4)))));
			Assert.That(underAMinute,
				Is.EqualTo(Resolve(Strings.NextEvent.StartsIn(duration: Strings.Countdown.UnderMinute()))));
			Assert.That(started, Is.EqualTo(Resolve(Strings.NextEvent.Now())));
		});
	}

	[Test]
	public async Task A_running_event_stays_shown_until_it_ends_by_default()
	{
		_harness.With(Event("running", Noon.AddMinutes(-10), TimeSpan.FromMinutes(30), title: "Running"))
			.With(Event("later", Noon.AddMinutes(30), TimeSpan.FromMinutes(30), title: "Later"));
		await _harness.SyncAsync();
		await using var session = await Open(new { whenStarted = "now" });

		var during = (Title(session), When(session));
		_harness.Time.Advance(TimeSpan.FromMinutes(20));
		var after = (Title(session), When(session));

		Assert.Multiple(() =>
		{
			Assert.That(during, Is.EqualTo(("Running", Resolve(Strings.NextEvent.Now()))));
			Assert.That(after,
				Is.EqualTo(("Later", Resolve(Strings.NextEvent.StartsIn(duration: Strings.Countdown.Minutes(10))))));
		});
	}

	[Test]
	public async Task With_show_next_a_running_event_is_skipped_for_the_next_one()
	{
		_harness.With(Event("running", Noon.AddMinutes(-10), TimeSpan.FromMinutes(30), title: "Running"))
			.With(Event("later", Noon.AddMinutes(30), TimeSpan.FromMinutes(30), title: "Later"));
		await _harness.SyncAsync();

		await using var session = await Open(new { whenStarted = "next" });

		Assert.Multiple(() =>
		{
			Assert.That(Title(session), Is.EqualTo("Later"));
			Assert.That(When(session),
				Is.EqualTo(Resolve(Strings.NextEvent.StartsIn(duration: Strings.Countdown.Minutes(30)))));
		});
	}

	[Test]
	public async Task All_day_events_are_skipped_unless_turned_on()
	{
		_harness.With(Event("holiday", new DateTimeOffset(Noon.Date, TimeSpan.Zero), TimeSpan.FromDays(1),
				title: "Holiday") with { IsAllDay = true })
			.With(Event("later", Noon.AddMinutes(30), TimeSpan.FromMinutes(30), title: "Later"));
		await _harness.SyncAsync();

		await using var byDefault = await Open();
		await using var withAllDay = await Open(new { showAllDay = true });

		Assert.Multiple(() =>
		{
			Assert.That(Title(byDefault), Is.EqualTo("Later"));
			Assert.That(Title(withAllDay), Is.EqualTo("Holiday"));
			Assert.That(Texts(withAllDay.BuildTree().Root), Does.Contain(Resolve(Strings.AllDay())));
		});
	}

	[Test]
	public async Task The_time_range_and_location_of_the_shown_event_are_listed()
	{
		_harness.With(Event("planning", Noon.AddHours(2), TimeSpan.FromHours(1), title: "Planning") with
		{
			Location = "Room 4",
		});
		await _harness.SyncAsync();

		await using var session = await Open();
		var texts = Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(texts, Does.Contain(Resolve(Strings.TimeRange(start: "14:00", end: "15:00"))));
			Assert.That(texts, Does.Contain("Room 4"));
		});
	}

	[Test]
	public async Task Without_an_upcoming_event_the_widget_says_so()
	{
		await _harness.SyncAsync();

		await using var session = await Open(new { showDate = false });

		Assert.That(Texts(session.BuildTree().Root), Is.EqualTo(new[] { Resolve(Strings.NoUpcomingEvents()) }));
	}

	[Test]
	public async Task With_the_date_on_a_small_red_date_line_tops_the_widget()
	{
		_harness.With(Event("planning", Noon.AddHours(2), TimeSpan.FromHours(1), title: "Planning"));
		await _harness.SyncAsync();

		await using var dated = await Open();
		await using var plain = await Open(new { showDate = false });
		var line = Get(dated.BuildTree().Root, "dateLine");

		Assert.Multiple(() =>
		{
			Assert.That(Texts(dated.BuildTree().Root)[0],
				Is.EqualTo(Resolve(Strings.DateLine(weekday: "THURSDAY", day: "1"))),
				"the date comes first");
			Assert.That(line.Properties[UiComponentProperties.Color].GetString(), Is.EqualTo("#ff3b30"));
			Assert.That(Title(dated), Is.EqualTo("Planning"));
			Assert.That(Find(plain.BuildTree().Root, "dateLine"), Is.Null);
		});
	}

	[Test]
	public async Task A_newly_synced_event_appears_without_reopening_the_widget()
	{
		await _harness.SyncAsync();
		await using var session = await Open();

		_harness.With(Event("late-add", Noon.AddMinutes(20), TimeSpan.FromMinutes(10), title: "Late add"));
		await _harness.SyncAsync();

		Assert.That(Title(session), Is.EqualTo("Late add"));
	}

	[Test]
	public async Task The_widget_leaves_every_press_to_its_flows_and_default_action()
	{
		_harness.With(Event("planning", Noon.AddHours(2), TimeSpan.FromHours(1), "team", "Planning"));
		await _harness.SyncAsync();
		await using var session = await Open();

		Assert.That(Flatten(session.BuildTree().Root).Where(IsPressable), Is.Empty);
	}

	private Task<IUiSession> Open(object? data = null)
		=> _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutNextEvent, data ?? new { });

	private static string? Title(IUiSession session)
		=> Find(session.BuildTree().Root, "title") is { } node ? Text(node) : null;

	private static string? When(IUiSession session, string culture = "en")
		=> Find(session.BuildTree().Root, "when") is { } node ? Text(node, culture) : null;
}
