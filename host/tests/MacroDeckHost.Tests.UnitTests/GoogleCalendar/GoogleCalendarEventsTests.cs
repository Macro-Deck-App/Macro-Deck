using System.Net;
using MacroDeck.Sdk.Calendar;

namespace MacroDeckHost.Tests.UnitTests.GoogleCalendar;

[TestFixture]
internal sealed class GoogleCalendarEventsTests
{
	private const string Primary = "me@example.com";
	private const string Holidays = "en.german#holiday@group.v.calendar.google.com";

	private static readonly DateTimeOffset _from = new(2026, 10, 5, 0, 0, 0, TimeSpan.FromHours(2));
	private static readonly DateTimeOffset _to = _from.AddDays(1);

	private GoogleCalendarHarness _harness = null!;
	private string _accountId = null!;

	[SetUp]
	public async Task SetUp()
	{
		_harness = new GoogleCalendarHarness();
		_accountId = _harness.AddAccount(Primary).ToString("D");
		await _harness.StartAsync();
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task A_timed_event_carries_its_times_place_meeting_link_and_guests()
	{
		_harness.Events(Primary,
			"""
			{"id":"evt-1","status":"confirmed","summary":"Standup","location":"Room 4","description":"<b>Agenda</b>",
			 "start":{"dateTime":"2026-10-05T10:00:00+02:00"},"end":{"dateTime":"2026-10-05T10:30:00+02:00"},
			 "hangoutLink":"https://meet.google.com/old-link",
			 "conferenceData":{"entryPoints":[{"entryPointType":"phone","uri":"tel:+1-555"},
			   {"entryPointType":"video","uri":"https://meet.google.com/abc-defg-hij"}]},
			 "attendees":[
			   {"email":"boss@example.com","displayName":"Boss","organizer":true,"responseStatus":"accepted"},
			   {"email":"me@example.com","self":true,"responseStatus":"needsAction"},
			   {"email":"dev@example.com","responseStatus":"declined"},
			   {"email":"maybe@example.com","responseStatus":"tentative"},
			   {"email":"room@resource.calendar.google.com","displayName":"Room 4","resource":true,"responseStatus":"accepted"}]}
			""");

		var events = await _harness.Integration.GetEventsAsync(_accountId,
			new CalendarEventQuery { From = _from, To = _to, CalendarIds = [Primary] },
			CancellationToken.None);

		var standup = events.Single();
		Assert.Multiple(() =>
		{
			Assert.That(standup.Id, Is.EqualTo("evt-1"));
			Assert.That(standup.CalendarId, Is.EqualTo(Primary));
			Assert.That(standup.Title, Is.EqualTo("Standup"));
			Assert.That(standup.Start, Is.EqualTo(new DateTimeOffset(2026, 10, 5, 8, 0, 0, TimeSpan.Zero)));
			Assert.That(standup.End, Is.EqualTo(new DateTimeOffset(2026, 10, 5, 8, 30, 0, TimeSpan.Zero)));
			Assert.That(standup.IsAllDay, Is.False);
			Assert.That(standup.Location, Is.EqualTo("Room 4"));
			Assert.That(standup.Description, Is.EqualTo("<b>Agenda</b>"));
			Assert.That(standup.MeetingUrl, Is.EqualTo("https://meet.google.com/abc-defg-hij"));
			Assert.That(standup.Participants.Select(p => (p.Name, p.Email, p.IsOrganizer, p.Response)),
				Is.EqualTo(new[]
				{
					("Boss", "boss@example.com", true, CalendarResponseStatus.Accepted),
					((string?)null, "me@example.com", false, CalendarResponseStatus.NeedsAction),
					(null, "dev@example.com", false, CalendarResponseStatus.Declined),
					(null, "maybe@example.com", false, CalendarResponseStatus.Tentative)
				}),
				"meeting rooms are resources, not people");
		});
	}

	[Test]
	public async Task An_all_day_event_covers_its_dates_with_an_exclusive_end()
	{
		_harness.Events(Primary,
			"""{"id":"holiday","summary":"Day off","start":{"date":"2026-10-05"},"end":{"date":"2026-10-06"}}""");

		var day = (await ReadAsync(Primary)).Single();

		Assert.Multiple(() =>
		{
			Assert.That(day.IsAllDay, Is.True);
			Assert.That(DateOnly.FromDateTime(day.Start.Date), Is.EqualTo(new DateOnly(2026, 10, 5)));
			Assert.That(DateOnly.FromDateTime(day.End.Date), Is.EqualTo(new DateOnly(2026, 10, 6)));
		});
	}

	[Test]
	public async Task Without_a_video_entry_point_the_hangout_link_is_the_meeting_link()
	{
		_harness.Events(Primary,
			"""
			{"id":"a","summary":"Call","start":{"dateTime":"2026-10-05T09:00:00Z"},"end":{"dateTime":"2026-10-05T09:30:00Z"},
			 "hangoutLink":"https://meet.google.com/xyz"},
			{"id":"b","summary":"Lunch","start":{"dateTime":"2026-10-05T12:00:00Z"},"end":{"dateTime":"2026-10-05T13:00:00Z"}}
			""");

		var events = await ReadAsync(Primary);

		Assert.That(events.Select(e => e.MeetingUrl), Is.EqualTo(new[] { "https://meet.google.com/xyz", null }));
	}

	[Test]
	public async Task Cancelled_events_are_left_out()
	{
		_harness.Events(Primary,
			"""
			{"id":"kept","status":"confirmed","summary":"Kept","start":{"dateTime":"2026-10-05T09:00:00Z"},"end":{"dateTime":"2026-10-05T10:00:00Z"}},
			{"id":"gone","status":"cancelled"}
			""");

		var events = await ReadAsync(Primary);

		Assert.That(events.Select(e => e.Id), Is.EqualTo(new[] { "kept" }));
	}

	[Test]
	public async Task Without_a_calendar_selection_every_page_of_every_calendar_is_read_for_the_range()
	{
		_harness.Server
			.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.CalendarListPath,
				HttpStatusCode.OK,
				$$"""{"items":[{"id":"{{Primary}}","summary":"Me","primary":true}],"nextPageToken":"list-2"}""")
			.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.CalendarListPath && r.Query.GetValueOrDefault("pageToken") == "list-2",
				HttpStatusCode.OK,
				$$"""{"items":[{"id":"{{Holidays}}","summary":"Holidays"}]}""")
			.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.EventsPath(Primary),
				HttpStatusCode.OK,
				"""{"items":[{"id":"p1","summary":"One","start":{"dateTime":"2026-10-05T09:00:00Z"},"end":{"dateTime":"2026-10-05T10:00:00Z"}}],"nextPageToken":"events-2"}""")
			.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.EventsPath(Primary) && r.Query.GetValueOrDefault("pageToken") == "events-2",
				HttpStatusCode.OK,
				"""{"items":[{"id":"p2","summary":"Two","start":{"dateTime":"2026-10-05T11:00:00Z"},"end":{"dateTime":"2026-10-05T12:00:00Z"}}]}""")
			.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.EventsPath(Holidays),
				HttpStatusCode.OK,
				"""{"items":[{"id":"h1","summary":"Holiday","start":{"date":"2026-10-05"},"end":{"date":"2026-10-06"}}]}""");

		var events = await _harness.Integration.GetEventsAsync(_accountId,
			new CalendarEventQuery { From = _from, To = _to },
			CancellationToken.None);

		var holidayRequest = _harness.Server.Requests.Single(r => r.Uri.AbsolutePath.EndsWith("/events", StringComparison.Ordinal) &&
			r.Uri.AbsolutePath.Contains("holiday", StringComparison.Ordinal));
		Assert.Multiple(() =>
		{
			Assert.That(events.Select(e => (e.CalendarId, e.Id)),
				Is.EquivalentTo(new[] { (Primary, "p1"), (Primary, "p2"), (Holidays, "h1") }));
			Assert.That(holidayRequest.Uri.AbsolutePath,
				Does.Contain("/calendars/en.german%23holiday%40group.v.calendar.google.com/events"),
				"a calendar id is one path segment");
			Assert.That(holidayRequest.Query["timeMin"], Is.EqualTo("2026-10-04T22:00:00.000Z"));
			Assert.That(holidayRequest.Query["timeMax"], Is.EqualTo("2026-10-05T22:00:00.000Z"));
			Assert.That(holidayRequest.Query["singleEvents"], Is.EqualTo("true"), "recurring events are expanded");
			Assert.That(holidayRequest.Query["orderBy"], Is.EqualTo("startTime"));
		});
	}

	[Test]
	public async Task A_calendar_selection_reads_only_the_selected_calendars()
	{
		_harness.Calendars($$"""{"id":"{{Primary}}"}""", $$"""{"id":"{{Holidays}}"}""")
			.Events(Primary)
			.Events(Holidays);

		await ReadAsync(Holidays);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Server.To(GoogleCalendarHarness.EventsPath(Primary)), Is.Empty);
			Assert.That(_harness.Server.To(GoogleCalendarHarness.EventsPath(Holidays)), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Calendars_are_listed_with_their_own_name_colour_and_primary_flag()
	{
		_harness.Calendars(
			$$"""{"id":"{{Primary}}","summary":"me@example.com","summaryOverride":"Me","backgroundColor":"#9fe1e7","primary":true}""",
			$$"""{"id":"{{Holidays}}","summary":"Holidays in Germany","backgroundColor":"teal"}""");

		var calendars = await _harness.Integration.GetCalendarsAsync(_accountId, CancellationToken.None);

		Assert.That(calendars.Select(c => (c.Id, c.Name, c.Color, c.IsPrimary)),
			Is.EqualTo(new[]
			{
				(Primary, "Me", "#9FE1E7", true),
				(Holidays, "Holidays in Germany", (string?)null, false)
			}));
	}

	[Test]
	public async Task Calendars_hidden_in_google_calendar_are_not_listed()
	{
		_harness.Calendars($$"""{"id":"{{Primary}}","primary":true}""",
			$$"""{"id":"{{Holidays}}","hidden":true}""");

		var calendars = await _harness.Integration.GetCalendarsAsync(_accountId, CancellationToken.None);

		Assert.That(calendars.Select(c => c.Id), Is.EqualTo(new[] { Primary }));
	}

	[Test]
	public async Task A_calendar_whose_access_was_removed_is_skipped_and_the_others_still_read()
	{
		_harness.Calendars($$"""{"id":"{{Primary}}","primary":true}""", $$"""{"id":"{{Holidays}}"}""")
			.Events(Primary,
				"""{"id":"p1","summary":"One","start":{"dateTime":"2026-10-05T09:00:00Z"},"end":{"dateTime":"2026-10-05T10:00:00Z"}}""");
		_harness.Server.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.EventsPath(Holidays),
			HttpStatusCode.NotFound,
			"{}");

		var events = await _harness.Integration.GetEventsAsync(_accountId,
			new CalendarEventQuery { From = _from, To = _to },
			CancellationToken.None);

		Assert.That(events.Select(e => e.Id), Is.EqualTo(new[] { "p1" }));
	}

	[Test]
	public void A_rate_limited_calendar_fails_the_read_instead_of_dropping_its_events()
	{
		_harness.Calendars($$"""{"id":"{{Primary}}","primary":true}""", $$"""{"id":"{{Holidays}}"}""")
			.Events(Primary,
				"""{"id":"p1","summary":"One","start":{"dateTime":"2026-10-05T09:00:00Z"},"end":{"dateTime":"2026-10-05T10:00:00Z"}}""");
		_harness.Server.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.EventsPath(Holidays),
			HttpStatusCode.Forbidden,
			"""{"error":{"code":403,"errors":[{"reason":"rateLimitExceeded"}]}}""");

		Assert.That(async () => await _harness.Integration.GetEventsAsync(_accountId,
				new CalendarEventQuery { From = _from, To = _to },
				CancellationToken.None),
			Throws.InstanceOf<HttpRequestException>());
	}

	[TestCase(HttpStatusCode.NotFound)]
	[TestCase(HttpStatusCode.Gone)]
	public async Task A_deleted_event_reads_as_gone(HttpStatusCode status)
	{
		_harness.Server.On(r => r.Uri.AbsolutePath.StartsWith(GoogleCalendarHarness.EventsPath(Primary) + "/", StringComparison.Ordinal),
			status,
			"""{"error":{"code":404}}""");

		var read = await _harness.Integration.GetEventAsync(_accountId, Primary, "evt-1", CancellationToken.None);

		Assert.That(read, Is.Null);
	}

	[Test]
	public async Task A_cancelled_event_reads_as_gone_and_a_live_one_with_its_details()
	{
		_harness.Server
			.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.EventsPath(Primary) + "/cancelled",
				HttpStatusCode.OK,
				"""{"id":"cancelled","status":"cancelled"}""")
			.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.EventsPath(Primary) + "/live",
				HttpStatusCode.OK,
				"""{"id":"live","summary":"Review","description":"Notes","start":{"dateTime":"2026-10-05T09:00:00Z"},"end":{"dateTime":"2026-10-05T10:00:00Z"}}""");

		var cancelled = await _harness.Integration.GetEventAsync(_accountId, Primary, "cancelled", CancellationToken.None);
		var live = await _harness.Integration.GetEventAsync(_accountId, Primary, "live", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(cancelled, Is.Null);
			Assert.That(live?.Description, Is.EqualTo("Notes"));
		});
	}

	[Test]
	public void A_failing_read_throws_instead_of_reporting_no_events()
	{
		_harness.Server.On(_ => true, HttpStatusCode.InternalServerError, """{"error":{"code":500}}""");

		Assert.Multiple(() =>
		{
			Assert.ThrowsAsync<HttpRequestException>(() => ReadAsync(Primary));
			Assert.ThrowsAsync<HttpRequestException>(() =>
				_harness.Integration.GetCalendarsAsync(_accountId, CancellationToken.None));
			Assert.ThrowsAsync<HttpRequestException>(() =>
				_harness.Integration.GetEventAsync(_accountId, Primary, "evt-1", CancellationToken.None));
		});
	}

	[Test]
	public void An_unknown_account_is_a_failure()
	{
		Assert.That(() => _harness.Integration.GetEventsAsync(Guid.NewGuid().ToString("D"),
				new CalendarEventQuery { From = _from, To = _to },
				CancellationToken.None),
			Throws.InstanceOf<InvalidOperationException>());
	}

	private Task<IReadOnlyList<CalendarEvent>> ReadAsync(string calendarId)
		=> _harness.Integration.GetEventsAsync(_accountId,
			new CalendarEventQuery { From = _from, To = _to, CalendarIds = [calendarId] },
			CancellationToken.None);
}
