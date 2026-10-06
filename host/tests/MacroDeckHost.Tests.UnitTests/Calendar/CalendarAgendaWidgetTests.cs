using System.Globalization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Nodes;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Calendar;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTree;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Calendar;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarAgendaWidgetTests
{
	private CalendarWidgetHarness _harness = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new CalendarWidgetHarness()
			.With(Event("standup", Noon.AddHours(1), TimeSpan.FromMinutes(30), title: "Standup") with
			{
				Location = "Room 1",
			})
			.With(Event("holiday", new DateTimeOffset(Noon.Date, TimeSpan.Zero), TimeSpan.FromDays(1), title: "Holiday") with { IsAllDay = true })
			.With(Event("review", Noon.AddHours(21), TimeSpan.FromHours(1), "team", "Review"))
			.With(Event("retro", Noon.AddDays(2).AddHours(-2), TimeSpan.FromHours(1), title: "Retro"));
	}

	[Test]
	public async Task A_large_agenda_shows_a_section_for_each_configured_day()
	{
		await _harness.SyncAsync();

		await using var session = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { days = 3 });
		var days = Days(session.BuildTree().Root);

		var saturday = new DateOnly(2026, 1, 3);
		var english = CultureInfo.GetCultureInfo("en");
		var thirdHeading = Resolve(Strings.DayHeading(weekday: saturday.ToString("dddd", english),
			date: saturday.ToString(english.DateTimeFormat.MonthDayPattern, english)));

		Assert.Multiple(() =>
		{
			Assert.That(days.Select(day => day.Heading),
				Is.EqualTo(new[] { Resolve(Strings.Today()), Resolve(Strings.Tomorrow()), thirdHeading }));
			Assert.That(days[0].Titles, Is.EqualTo(new[] { "Holiday", "Standup" }), "all-day events come first");
			Assert.That(days[1].Titles, Is.EqualTo(new[] { "Review" }));
			Assert.That(days[2].Titles, Is.EqualTo(new[] { "Retro" }));
		});
	}

	[Test]
	public async Task The_day_count_limits_how_far_ahead_the_agenda_reaches()
	{
		await _harness.SyncAsync();

		await using var session = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { days = 2 });
		var days = Days(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(days, Has.Count.EqualTo(2));
			Assert.That(days.SelectMany(day => day.Titles), Does.Not.Contain("Retro"));
		});
	}

	[Test]
	public async Task The_compact_layout_lists_the_next_events_within_the_configured_days()
	{
		await _harness.SyncAsync();

		await using var week = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { days = 7 });
		await using var today = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { days = 1 });
		var compact = Get(week.BuildTree().Root, "compact");

		Assert.Multiple(() =>
		{
			Assert.That(Titles(compact), Is.EqualTo(new[] { "Holiday", "Standup", "Review", "Retro" }));
			Assert.That(Texts(compact), Has.Some.Contains(Resolve(Strings.Tomorrow())));
			Assert.That(Titles(Get(today.BuildTree().Root, "compact")), Is.EqualTo(new[] { "Holiday", "Standup" }));
		});
	}

	[Test]
	public async Task All_day_events_are_left_out_when_turned_off()
	{
		await _harness.SyncAsync();

		await using var session = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { days = 1, showAllDay = false });
		var root = session.BuildTree().Root;

		Assert.Multiple(() =>
		{
			Assert.That(Titles(Get(root, "compact")), Is.EqualTo(new[] { "Standup" }));
			Assert.That(Days(root)[0].Titles, Is.EqualTo(new[] { "Standup" }));
		});
	}

	[Test]
	public async Task The_field_toggles_decide_what_each_row_shows()
	{
		await _harness.SyncAsync();

		await using var plain = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { showTime = false, showLocation = false, showCalendar = false });
		await using var detailed = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { showTime = true, showLocation = true, showCalendar = true });

		var plainTexts = Texts(Get(plain.BuildTree().Root, "compact"));
		var detailedTexts = Texts(Get(detailed.BuildTree().Root, "compact"));

		Assert.Multiple(() =>
		{
			Assert.That(plainTexts, Is.EqualTo(new[] { "Holiday", "Standup" }));
			Assert.That(detailedTexts, Does.Contain(Resolve(Strings.AllDay())));
			Assert.That(detailedTexts, Does.Contain("13:00"));
			Assert.That(detailedTexts, Does.Contain("Room 1"));
			Assert.That(detailedTexts, Does.Contain("Calendar main"));
		});
	}

	[Test]
	public async Task A_changed_time_format_reaches_an_open_agenda_within_five_minutes()
	{
		await _harness.SyncAsync();
		await using var session = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { showTime = true });
		var before = Texts(Get(session.BuildTree().Root, "compact"));

		_harness.Preferences.TimeFormat = AppPreferenceService.TimeFormat12h;
		_harness.Time.Advance(TimeSpan.FromMinutes(5));

		Assert.That(before, Does.Contain("13:00"));
		await WaitForAsync(() => Texts(Get(session.BuildTree().Root, "compact"))
				.Any(text => text.StartsWith("1:00", StringComparison.Ordinal) && text.EndsWith("PM", StringComparison.Ordinal)),
			"the agenda kept the old time format");
	}

	[Test]
	public async Task A_day_without_events_says_so_and_the_dated_agenda_says_none_are_coming()
	{
		_harness.Google.ClearEvents("alice");
		await _harness.SyncAsync();

		await using var plain = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { days = 3, showDate = false });
		await using var dated = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { days = 3 });
		var plainRoot = plain.BuildTree().Root;
		var datedRoot = dated.BuildTree().Root;

		Assert.Multiple(() =>
		{
			Assert.That(Texts(Get(plainRoot, "compact")), Is.EqualTo(new[] { Resolve(Strings.NoEvents()) }));
			Assert.That(Texts(Get(plainRoot, "sections")), Is.EqualTo(new[] { Resolve(Strings.NoEvents()) }));
			Assert.That(Texts(Get(datedRoot, "compact")), Is.EqualTo(new[] { Resolve(Strings.NoUpcomingEvents()) }));
			Assert.That(Texts(Get(datedRoot, "glance")), Does.Contain(Resolve(Strings.NoUpcomingEvents())));
		});
	}

	[Test]
	public async Task With_the_date_on_every_size_is_headed_by_the_weekday_in_red_over_the_day_number()
	{
		await _harness.SyncAsync();

		await using var dated = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { days = 2 });
		await using var plain = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { days = 2, showDate = false });
		var root = dated.BuildTree().Root;

		Assert.Multiple(() =>
		{
			foreach (var size in new[] { "glance", "dateCompact", "dateSections" })
			{
				var weekday = Get(Get(root, size), "weekday");
				Assert.That(Text(weekday), Is.EqualTo("THURSDAY"), size);
				Assert.That(weekday.Properties[UiComponentProperties.Color].GetString(), Is.EqualTo("#ff3b30"), size);
				Assert.That(Text(Get(Get(root, size), "dayNumber")), Is.EqualTo("1"), size);
			}

			Assert.That(Find(plain.BuildTree().Root, "weekday"), Is.Null);
			Assert.That(Find(plain.BuildTree().Root, "dayNumber"), Is.Null);
			Assert.That(Texts(Get(plain.BuildTree().Root, "compactNarrow")), Does.Contain("Standup"));
		});
	}

	[Test]
	public async Task The_one_cell_agenda_lists_the_next_two_events_under_the_date()
	{
		await _harness.SyncAsync();

		await using var week = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { days = 7 });
		await using var timed = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { days = 7, showAllDay = false });
		var glance = Get(week.BuildTree().Root, "glance");
		var timedGlance = Get(timed.BuildTree().Root, "glance");

		Assert.Multiple(() =>
		{
			Assert.That(Titles(glance), Is.EqualTo(new[] { "Holiday", "Standup" }));
			Assert.That(Texts(glance), Does.Contain(Resolve(Strings.AllDay())).And.Contain("13:00"));
			Assert.That(Titles(timedGlance), Is.EqualTo(new[] { "Standup", "Review" }));
			Assert.That(Texts(timedGlance), Has.Some.Contains(Resolve(Strings.Tomorrow())),
				"an event of a later day says which day");
		});
	}

	[Test]
	public async Task Once_nothing_is_left_today_the_dated_agenda_says_so_or_shows_what_comes_next()
	{
		await _harness.SyncAsync();
		await using var today = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { days = 1, showAllDay = false });
		await using var twoDays = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { days = 2, showAllDay = false });

		_harness.Time.Advance(TimeSpan.FromHours(2));
		var todayGlance = Get(today.BuildTree().Root, "glance");
		var twoDaysGlance = Get(twoDays.BuildTree().Root, "glance");

		Assert.Multiple(() =>
		{
			Assert.That(Texts(todayGlance), Does.Contain(Resolve(Strings.NoMoreEventsToday())));
			Assert.That(Titles(todayGlance), Is.Empty);
			Assert.That(Titles(twoDaysGlance), Is.EqualTo(new[] { "Review" }));
			Assert.That(Texts(twoDaysGlance), Has.Some.Contains(Resolve(Strings.Tomorrow())));
			Assert.That(Texts(twoDaysGlance), Does.Not.Contain(Resolve(Strings.NoMoreEventsToday())));
		});
	}

	[Test]
	public async Task Without_a_connected_calendar_the_widget_asks_for_one()
	{
		var harness = new CalendarWidgetHarness();
		harness.Google.Accounts.Clear();
		await harness.SyncAsync();

		await using var session = await harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { });

		Assert.That(Texts(session.BuildTree().Root), Is.EqualTo(new[] { Resolve(Strings.NoCalendars()) }));
	}

	[Test]
	public async Task A_failing_account_shows_a_hint_once_the_next_sync_fails()
	{
		await _harness.SyncAsync();
		await using var session = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { });
		var before = Texts(session.BuildTree().Root);

		_harness.Google.FailingAccounts.Add("alice");
		await _harness.SyncAsync();
		var after = Texts(session.BuildTree().Root);

		Assert.Multiple(() =>
		{
			Assert.That(before, Does.Not.Contain(Resolve(Strings.AccountError())));
			Assert.That(after, Does.Contain(Resolve(Strings.AccountError())));
			Assert.That(Titles(Get(session.BuildTree().Root, "compact")), Does.Contain("Standup"),
				"the last synced events stay visible");
		});
	}

	[Test]
	public async Task A_hint_for_a_failing_account_is_not_shown_on_a_widget_that_does_not_use_it()
	{
		var harness = new CalendarWidgetHarness();
		harness.Google.WithAccount("bob", "work");
		await harness.SyncAsync();
		harness.Google.FailingAccounts.Add("bob");
		await harness.SyncAsync();

		await using var session = await harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { calendars = new[] { CalendarWidgetHarness.CalendarKey() } });

		Assert.That(Texts(session.BuildTree().Root), Does.Not.Contain(Resolve(Strings.AccountError())));
	}

	[Test]
	public async Task Only_the_selected_calendars_are_shown()
	{
		await _harness.SyncAsync();

		await using var session = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { days = 2, calendars = new[] { CalendarWidgetHarness.CalendarKey("team") } });

		Assert.That(Days(session.BuildTree().Root).SelectMany(day => day.Titles), Is.EqualTo(new[] { "Review" }));
	}

	[Test]
	public async Task At_local_midnight_today_moves_to_the_next_day()
	{
		await _harness.SyncAsync();
		await using var session = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { days = 2 });

		_harness.Time.Advance(TimeSpan.FromHours(12));
		var root = session.BuildTree().Root;
		var days = Days(root);

		Assert.Multiple(() =>
		{
			Assert.That(Titles(Get(root, "compact")), Is.EqualTo(new[] { "Review", "Retro" }));
			Assert.That(Text(Get(Get(root, "glance"), "weekday")), Is.EqualTo("FRIDAY"), "the date header moves on too");
			Assert.That(Text(Get(Get(root, "glance"), "dayNumber")), Is.EqualTo("2"));
			Assert.That(days[0].Heading, Is.EqualTo(Resolve(Strings.Today())));
			Assert.That(days[0].Titles, Is.EqualTo(new[] { "Review" }));
			Assert.That(days[1].Titles, Is.EqualTo(new[] { "Retro" }));
		});
	}

	[Test]
	public async Task An_event_that_has_ended_drops_off_the_agenda()
	{
		await _harness.SyncAsync();
		await using var session = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda,
			new { showAllDay = false });

		_harness.Time.Advance(TimeSpan.FromMinutes(90));

		Assert.That(Texts(Get(session.BuildTree().Root, "compact")),
			Is.EqualTo(new[] { Resolve(Strings.NoMoreEventsToday()) }));
	}

	[Test]
	public async Task No_event_row_claims_a_press_so_the_whole_tile_stays_pressable()
	{
		await _harness.SyncAsync();
		await using var session = await _harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { days = 2 });
		var root = session.BuildTree().Root;

		Assert.Multiple(() =>
		{
			Assert.That(Texts(root), Does.Contain("Review"));
			Assert.That(Flatten(root).Where(IsPressable), Is.Empty);
		});
	}

	[Test]
	public async Task The_picker_sample_shows_events_without_any_calendar_connected()
	{
		var harness = new CalendarWidgetHarness();
		harness.Google.Accounts.Clear();
		await harness.SyncAsync();

		await using var sample = await harness.OpenWidgetAsync(CalendarWidgetTypes.LayoutAgenda, new { },
			sample: true);
		var root = sample.BuildTree().Root;

		Assert.Multiple(() =>
		{
			Assert.That(Titles(Get(root, "compact")), Does.Contain(Resolve(Strings.Sample.Standup())));
			Assert.That(Flatten(root).Where(IsPressable), Is.Empty);
		});
	}

	private static List<(string Heading, List<string> Titles)> Days(UiNode root)
		=> [.. Get(Get(root, "sections"), "days").Children
			.Select(day => (Text(Get(day, "heading"))!, Titles(day)))];

	private static List<string> Titles(UiNode root)
		=> [.. Flatten(root).Where(node => node.Id.EndsWith(".title", StringComparison.Ordinal)).Select(node => Text(node)!)];
}
