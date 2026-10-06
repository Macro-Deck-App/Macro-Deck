using MacroDeck.Sdk.Calendar;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using static MacroDeckHost.Tests.UnitTests.Calendar.CalendarTesting;

namespace MacroDeckHost.Tests.UnitTests.Calendar;

[TestFixture]
public class CalendarEventCacheTests
{
	private FakeTimeProvider _time = null!;

	[SetUp]
	public void SetUp() => _time = new FakeTimeProvider { Now = Noon };

	[Test]
	public async Task The_cache_holds_the_events_from_yesterday_to_a_week_ahead_and_nothing_else()
	{
		var provider = new FakeCalendarIntegration("app.google").WithAccount("alice", "main");
		provider.IgnoresRange = true;
		provider.WithEvent("alice", Event("too-old", Noon.AddDays(-2), TimeSpan.FromHours(1)))
			.WithEvent("alice", Event("yesterday", Noon.AddDays(-1), TimeSpan.FromHours(1)))
			.WithEvent("alice", Event("today", Noon.AddHours(1), TimeSpan.FromHours(1)))
			.WithEvent("alice", Event("last-day", Noon.AddDays(7).AddHours(11), TimeSpan.FromMinutes(30)))
			.WithEvent("alice", Event("too-far", Noon.AddDays(7).AddHours(12), TimeSpan.FromHours(1)));
		var cache = Cache(_time, integrations: provider);

		await cache.SyncAsync(CancellationToken.None);

		var snapshot = cache.Snapshot;
		Assert.Multiple(() =>
		{
			Assert.That(snapshot.WindowStart, Is.EqualTo(new DateTimeOffset(2025, 12, 31, 0, 0, 0, TimeSpan.Zero)));
			Assert.That(snapshot.WindowEnd, Is.EqualTo(new DateTimeOffset(2026, 1, 9, 0, 0, 0, TimeSpan.Zero)));
			Assert.That(snapshot.Events.Select(e => e.EventId), Is.EqualTo(new[] { "yesterday", "today", "last-day" }));
		});
	}

	[Test]
	public async Task Events_across_the_whole_window_reach_the_snapshot_even_when_a_plugin_truncates_a_busy_day()
	{
		var plugin = new FakeCalendarIntegration("community.calendar").WithAccount("alice", "main");
		plugin.MaxEventsPerReply = 2;
		var builtIn = new FakeCalendarIntegration("app.google").WithAccount("bob", "main");
		foreach (var day in new[] { -1, 0, 3, 7 })
		{
			plugin.WithEvent("alice", Event($"p{day}", Noon.AddDays(day), TimeSpan.FromHours(1)));
			builtIn.WithEvent("bob", Event($"b{day}", Noon.AddDays(day), TimeSpan.FromHours(1)));
		}

		plugin.WithEvent("alice", Event("busy-1", Noon.AddDays(3).AddHours(1), TimeSpan.FromHours(1)))
			.WithEvent("alice", Event("busy-2", Noon.AddDays(3).AddHours(2), TimeSpan.FromHours(1)));
		var registry = new ConfigurableIntegrationRegistry([plugin, builtIn], plugins: ["community.calendar"]);
		var cache = new CalendarEventCache(new CalendarRegistry(registry, Logger()), _time, Logger(), TimeZoneInfo.Utc);

		await cache.SyncAsync(CancellationToken.None);

		Assert.That(cache.Snapshot.Events.Select(e => e.EventId),
			Is.EquivalentTo(new[] { "p-1", "b-1", "p0", "b0", "p3", "busy-1", "b3", "p7", "b7" }));
	}

	[Test]
	public async Task Events_are_merged_across_accounts_with_qualified_account_and_calendar()
	{
		var provider = new FakeCalendarIntegration("app.google", "Google Calendar")
			.WithAccount("alice", "main")
			.WithAccount("bob", "team")
			.WithEvent("alice",
				Event("a1", Noon.AddHours(2), TimeSpan.FromHours(1), meetingUrl: "https://meet.example.com/a1"))
			.WithEvent("bob",
				Event("b1", Noon.AddHours(1), TimeSpan.FromHours(1), "team", meetingUrl: "javascript:alert(1)"));
		var cache = Cache(_time, integrations: provider);

		await cache.SyncAsync(CancellationToken.None);

		var events = cache.Snapshot.Events;
		Assert.Multiple(() =>
		{
			Assert.That(events.Select(e => e.EventId), Is.EqualTo(new[] { "b1", "a1" }), "sorted by start");
			Assert.That(events[0].AccountId, Is.EqualTo("app.google::bob"));
			Assert.That(events[0].AccountName, Is.EqualTo("bob@example.com"));
			Assert.That(events[0].CalendarName, Is.EqualTo("Calendar team"));
			Assert.That(CalendarKeys.TryParseCalendar(events[0].CalendarKey, out var account, out var calendar),
				Is.True);
			Assert.That((account, calendar), Is.EqualTo(("app.google::bob", "team")));
			Assert.That(events[0].MeetingUrl, Is.Null, "only a web link counts as a meeting link");
			Assert.That(events[1].MeetingUrl, Is.EqualTo("https://meet.example.com/a1"));
		});
	}

	[Test]
	public async Task An_all_day_event_covers_its_dates_in_the_hosts_time_zone()
	{
		var zone = TimeZoneInfo.CreateCustomTimeZone("Test+2", TimeSpan.FromHours(2), "Test+2", "Test+2");
		var provider = new FakeCalendarIntegration("app.google")
			.WithAccount("alice", "main")
			.WithEvent("alice",
				new CalendarEvent
				{
					Id = "holiday",
					CalendarId = "main",
					Title = "Holiday",
					Start = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.FromHours(9)),
					End = new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.FromHours(9)),
					IsAllDay = true
				});
		provider.IgnoresRange = true;
		var cache = Cache(_time, zone, provider);

		await cache.SyncAsync(CancellationToken.None);

		var holiday = cache.Snapshot.Events.Single();
		Assert.Multiple(() =>
		{
			Assert.That(holiday.Start, Is.EqualTo(new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.FromHours(2))));
			Assert.That(holiday.End, Is.EqualTo(new DateTimeOffset(2026, 1, 3, 0, 0, 0, TimeSpan.FromHours(2))));
		});
	}

	[Test]
	public async Task A_failing_account_keeps_its_last_events_and_reports_an_error_without_affecting_others()
	{
		var provider = new FakeCalendarIntegration("app.google")
			.WithAccount("alice", "main")
			.WithAccount("bob", "main")
			.WithEvent("alice", Event("a1", Noon.AddHours(1), TimeSpan.FromHours(1)))
			.WithEvent("bob", Event("b1", Noon.AddHours(2), TimeSpan.FromHours(1)));
		var cache = Cache(_time, integrations: provider);
		await cache.SyncAsync(CancellationToken.None);

		var changes = 0;
		cache.Changed += () => changes++;
		provider.FailingAccounts.Add("bob");
		provider.ClearEvents("alice");
		provider.WithEvent("alice", Event("a2", Noon.AddHours(3), TimeSpan.FromHours(1)));

		await cache.SyncAsync(CancellationToken.None);

		var snapshot = cache.Snapshot;
		Assert.Multiple(() =>
		{
			Assert.That(snapshot.Events.Select(e => e.EventId), Is.EqualTo(new[] { "b1", "a2" }));
			Assert.That(snapshot.Accounts.Select(a => (a.Account.AccountId, a.Status)),
				Is.EqualTo(new[]
				{
					("app.google::alice", CalendarAccountStatus.Ok), ("app.google::bob", CalendarAccountStatus.Error)
				}));
			Assert.That(snapshot.Accounts[1].Calendars, Has.Count.EqualTo(1), "the last known calendars stay");
			Assert.That(changes, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Event_details_read_the_provider_and_show_the_description_as_plain_text()
	{
		var provider = new FakeCalendarIntegration("app.google")
			.WithAccount("alice", "main")
			.WithEvent("alice", Event("a1", Noon.AddHours(1), TimeSpan.FromHours(1)));
		provider.Details["a1"] = Event("a1", Noon.AddHours(1), TimeSpan.FromHours(1)) with
		{
			Description = "<p>Agenda:<br>1. Q&amp;A</p><script>x</script>",
			Participants = [new CalendarParticipant { Name = "Bob", IsOrganizer = true }]
		};
		var cache = Cache(_time, integrations: provider);
		await cache.SyncAsync(CancellationToken.None);
		var key = cache.Snapshot.Events.Single().CalendarKey;

		var details = await cache.GetEventDetailsAsync(key, "a1", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(details!.Description, Is.EqualTo("Agenda:\n1. Q&A"));
			Assert.That(details.Participants.Single().Name, Is.EqualTo("Bob"));
		});
	}

	[Test]
	public async Task Event_details_fall_back_to_the_synced_summary_when_the_read_fails_and_are_null_when_gone()
	{
		var provider = new FakeCalendarIntegration("app.google")
			.WithAccount("alice", "main")
			.WithEvent("alice", Event("a1", Noon.AddHours(1), TimeSpan.FromHours(1)));
		var cache = Cache(_time, integrations: provider);
		await cache.SyncAsync(CancellationToken.None);
		var key = cache.Snapshot.Events.Single().CalendarKey;

		var gone = await cache.GetEventDetailsAsync(key, "a1", CancellationToken.None);
		provider.DetailsFailure = new HttpRequestException("offline");
		var fallback = await cache.GetEventDetailsAsync(key, "a1", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(gone, Is.Null);
			Assert.That(fallback!.Summary.Title, Is.EqualTo("Event a1"));
			Assert.That(fallback.Description, Is.Null);
		});
	}
}
