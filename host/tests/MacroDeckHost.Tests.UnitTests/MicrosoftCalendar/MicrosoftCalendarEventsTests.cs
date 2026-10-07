using System.Net;
using MacroDeck.Sdk.Calendar;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.MicrosoftCalendar;

[TestFixture]
internal sealed class MicrosoftCalendarEventsTests
{
	private const string Email = "me@contoso.com";

	private static readonly DateTimeOffset _from = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
	private static readonly DateTimeOffset _to = new(2026, 1, 15, 0, 0, 0, TimeSpan.Zero);

	private MicrosoftCalendarHarness _harness = null!;
	private string _account = string.Empty;

	[SetUp]
	public async Task SetUp()
	{
		_harness = new MicrosoftCalendarHarness();
		_harness.Calendars(MicrosoftCalendarHarness.Calendar("cal-1", "Calendar", isDefault: true),
			MicrosoftCalendarHarness.Calendar("cal-2", "Birthdays", hexColor: ""),
			MicrosoftCalendarHarness.Calendar("cal-3", "Team", ownerAddress: "alex@contoso.com", ownerName: "Alex Wilber"));
		_account = _harness.AddAccount(Email, ["cal-1", "cal-3"]).ToString("D");
		await _harness.StartAsync();
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task A_timed_meeting_carries_its_times_place_teams_link_organizer_and_attendees()
	{
		_harness.Events("cal-1", """
			{
			  "id": "AAMkAGIAAAoZDOFAAA=",
			  "subject": "Plan summer company picnic",
			  "isAllDay": false,
			  "isCancelled": false,
			  "originalStartTimeZone": "Pacific Standard Time",
			  "start": { "dateTime": "2026-01-05T17:30:00.0000000", "timeZone": "UTC" },
			  "end": { "dateTime": "2026-01-05T18:00:00.0000000", "timeZone": "UTC" },
			  "location": { "displayName": "Conf Room 3", "locationType": "default" },
			  "onlineMeeting": { "joinUrl": "https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc" },
			  "onlineMeetingUrl": null,
			  "organizer": { "emailAddress": { "name": "Megan Bowen", "address": "me@contoso.com" } },
			  "attendees": [
			    { "type": "required", "status": { "response": "organizer", "time": "0001-01-01T00:00:00Z" },
			      "emailAddress": { "name": "Megan Bowen", "address": "ME@contoso.com" } },
			    { "type": "required", "status": { "response": "accepted", "time": "2026-01-02T10:00:00Z" },
			      "emailAddress": { "name": "Alex Wilber", "address": "alex@contoso.com" } },
			    { "type": "optional", "status": { "response": "tentativelyAccepted", "time": "2026-01-02T10:00:00Z" },
			      "emailAddress": { "name": "Adele Vance", "address": "adele@contoso.com" } },
			    { "type": "required", "status": { "response": "none", "time": "0001-01-01T00:00:00Z" },
			      "emailAddress": { "name": "Lee Gu", "address": "lee@contoso.com" } },
			    { "type": "required", "status": { "response": "declined", "time": "2026-01-02T10:00:00Z" },
			      "emailAddress": { "name": "Diego Siciliani", "address": "diego@contoso.com" } },
			    { "type": "resource", "status": { "response": "accepted", "time": "2026-01-02T10:00:00Z" },
			      "emailAddress": { "name": "Conf Room 3", "address": "room3@contoso.com" } }
			  ]
			}
			""");

		var meeting = (await ReadAsync(["cal-1"])).Single();
		var request = _harness.Server.To(MicrosoftCalendarHarness.CalendarViewPath("cal-1")).Single();

		Assert.Multiple(() =>
		{
			Assert.That(meeting.Title, Is.EqualTo("Plan summer company picnic"));
			Assert.That(meeting.CalendarId, Is.EqualTo("cal-1"));
			Assert.That(meeting.Start, Is.EqualTo(new DateTimeOffset(2026, 1, 5, 17, 30, 0, TimeSpan.Zero)));
			Assert.That(meeting.End, Is.EqualTo(new DateTimeOffset(2026, 1, 5, 18, 0, 0, TimeSpan.Zero)));
			Assert.That(meeting.IsAllDay, Is.False);
			Assert.That(meeting.Location, Is.EqualTo("Conf Room 3"));
			Assert.That(meeting.MeetingUrl, Is.EqualTo("https://teams.microsoft.com/l/meetup-join/19%3ameeting_abc"));
			Assert.That(meeting.Participants.Select(p => (p.Name, p.IsOrganizer, p.Response)),
				Is.EqualTo(new[]
				{
					("Megan Bowen", true, CalendarResponseStatus.Accepted),
					("Alex Wilber", false, CalendarResponseStatus.Accepted),
					("Adele Vance", false, CalendarResponseStatus.Tentative),
					("Lee Gu", false, CalendarResponseStatus.NeedsAction),
					("Diego Siciliani", false, CalendarResponseStatus.Declined)
				}));
			Assert.That(request.Query["startDateTime"], Is.EqualTo("2026-01-01T00:00:00Z"));
			Assert.That(request.Query["endDateTime"], Is.EqualTo("2026-01-15T00:00:00Z"));
			Assert.That(request.Query["$select"].Split(','), Does.Not.Contain("body"), "the list stays small");
			Assert.That(request.Prefer, Does.Contain("outlook.timezone=\"UTC\""));
			Assert.That(request.Authorization, Is.EqualTo("Bearer access-0"));
		});
	}

	[Test]
	public async Task Without_a_teams_meeting_the_online_meeting_url_is_the_meeting_link()
	{
		_harness.Events("cal-1",
			Event("e1", "2026-01-05T09:00:00.0000000", "2026-01-05T10:00:00.0000000",
				extra: """ "onlineMeeting": null, "onlineMeetingUrl": "https://zoom.us/j/123" """));

		Assert.That((await ReadAsync(["cal-1"])).Single().MeetingUrl, Is.EqualTo("https://zoom.us/j/123"));
	}

	[TestCase("2026-01-05T00:00:00.0000000", "2026-01-06T00:00:00.0000000", "W. Europe Standard Time",
		TestName = "An all-day event returned as a floating midnight keeps its date")]
	[TestCase("2026-01-04T23:00:00.0000000", "2026-01-05T23:00:00.0000000", "W. Europe Standard Time",
		TestName = "An all-day event of a zone east of UTC keeps its date")]
	[TestCase("2026-01-05T08:00:00.0000000", "2026-01-06T08:00:00.0000000", "Pacific Standard Time",
		TestName = "An all-day event of a zone west of UTC keeps its date")]
	[TestCase("2026-01-04T23:00:00.0000000", "2026-01-05T23:00:00.0000000", "tzone://Microsoft/Custom",
		TestName = "An all-day event of an unknown zone keeps its date")]
	public async Task All_day_events_cover_their_dates_with_an_exclusive_end(string start, string end, string zone)
	{
		_harness.Events("cal-1",
			Event("day", start, end, extra: $$""" "isAllDay": true, "originalStartTimeZone": "{{zone}}" """));

		var day = (await ReadAsync(["cal-1"])).Single();

		Assert.Multiple(() =>
		{
			Assert.That(day.IsAllDay, Is.True);
			Assert.That(day.Start, Is.EqualTo(new DateTimeOffset(2026, 1, 5, 0, 0, 0, TimeSpan.Zero)));
			Assert.That(day.End, Is.EqualTo(new DateTimeOffset(2026, 1, 6, 0, 0, 0, TimeSpan.Zero)));
		});
	}

	[Test]
	public async Task Each_occurrence_of_a_recurring_meeting_is_its_own_event()
	{
		_harness.Events("cal-1",
			Event("occ-1", "2026-01-05T09:00:00.0000000", "2026-01-05T09:15:00.0000000",
				extra: """ "type": "occurrence", "seriesMasterId": "master" """),
			Event("occ-2", "2026-01-06T09:00:00.0000000", "2026-01-06T09:15:00.0000000",
				extra: """ "type": "exception", "seriesMasterId": "master" """),
			Event("occ-3", "2026-01-07T09:00:00.0000000", "2026-01-07T09:15:00.0000000",
				extra: """ "type": "occurrence", "seriesMasterId": "master" """));

		var events = await ReadAsync(["cal-1"]);

		Assert.That(events.Select(e => e.Start.Day), Is.EqualTo(new[] { 5, 6, 7 }));
	}

	[Test]
	public async Task Cancelled_events_are_left_out()
	{
		_harness.Events("cal-1",
			Event("kept", "2026-01-05T09:00:00.0000000", "2026-01-05T10:00:00.0000000"),
			Event("gone", "2026-01-06T09:00:00.0000000", "2026-01-06T10:00:00.0000000",
				extra: """ "isCancelled": true """));

		Assert.That((await ReadAsync(["cal-1"])).Select(e => e.Id), Is.EqualTo(new[] { "kept" }));
	}

	[Test]
	public async Task Every_page_is_read_but_a_next_link_off_graph_is_not_followed()
	{
		const string page2 = "https://graph.microsoft.com/v1.0/me/calendars/cal-1/calendarView?$skiptoken=p2";
		_harness.Server
			.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.CalendarViewPath("cal-1"),
				HttpStatusCode.OK,
				$$"""{"value":[{{Event("a", "2026-01-05T09:00:00.0000000", "2026-01-05T10:00:00.0000000")}}],"@odata.nextLink":"{{page2}}"}""")
			.On(r => r.Uri.Query.Contains("skiptoken=p2", StringComparison.Ordinal),
				HttpStatusCode.OK,
				$$"""{"value":[{{Event("b", "2026-01-06T09:00:00.0000000", "2026-01-06T10:00:00.0000000")}}],"@odata.nextLink":"https://attacker.example/steal"}""");

		var events = await ReadAsync(["cal-1"]);

		Assert.Multiple(() =>
		{
			Assert.That(events.Select(e => e.Id), Is.EqualTo(new[] { "a", "b" }));
			Assert.That(_harness.Server.Requests.Select(r => r.Uri.Host), Has.None.EqualTo("attacker.example"));
		});
	}

	[Test]
	public async Task Only_the_chosen_calendars_are_offered_and_read()
	{
		_harness.Events("cal-1", Event("mine", "2026-01-05T09:00:00.0000000", "2026-01-05T10:00:00.0000000"))
			.Events("cal-2", Event("hidden", "2026-01-05T09:00:00.0000000", "2026-01-05T10:00:00.0000000"))
			.Events("cal-3", Event("team", "2026-01-05T11:00:00.0000000", "2026-01-05T12:00:00.0000000"));

		var calendars = await _harness.Integration.GetCalendarsAsync(_account, CancellationToken.None);
		var all = await ReadAsync([]);
		var explicitDeselected = await ReadAsync(["cal-2"]);

		Assert.Multiple(() =>
		{
			Assert.That(calendars.Select(c => (c.Id, c.Name, c.Color, c.IsPrimary)),
				Is.EqualTo(new[] { ("cal-1", "Calendar", (string?)"#E3008C", true), ("cal-3", "Team", "#E3008C", false) }));
			Assert.That(all.Select(e => e.Id), Is.EquivalentTo(new[] { "mine", "team" }));
			Assert.That(explicitDeselected, Is.Empty);
			Assert.That(_harness.Server.To(MicrosoftCalendarHarness.CalendarViewPath("cal-2")), Is.Empty);
		});
	}

	[Test]
	public async Task A_throttled_account_waits_for_retry_after_before_asking_graph_again()
	{
		_harness.Server.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.CalendarViewPath("cal-1"),
			_ => new GraphReply(HttpStatusCode.TooManyRequests,
				"""{"error":{"code":"TooManyRequests","message":"Please retry again later."}}""",
				TimeSpan.FromSeconds(30)));

		Assert.CatchAsync<HttpRequestException>(() => ReadAsync(["cal-1"]));
		_harness.Time.Advance(TimeSpan.FromSeconds(10));
		Assert.CatchAsync<HttpRequestException>(() => ReadAsync(["cal-1"]));
		var whileWaiting = _harness.Server.Requests.Count;

		_harness.Events("cal-1", Event("e", "2026-01-05T09:00:00.0000000", "2026-01-05T10:00:00.0000000"));
		_harness.Time.Advance(TimeSpan.FromSeconds(25));
		var events = await ReadAsync(["cal-1"]);

		Assert.Multiple(() =>
		{
			Assert.That(whileWaiting, Is.EqualTo(1), "nothing is sent while Graph asked to wait");
			Assert.That(events.Select(e => e.Id), Is.EqualTo(new[] { "e" }));
		});
	}

	[TestCase(HttpStatusCode.NotFound, "ErrorItemNotFound")]
	[TestCase(HttpStatusCode.Forbidden, "ErrorAccessDenied")]
	public async Task A_calendar_that_can_no_longer_be_read_is_reported_while_the_others_still_read(
		HttpStatusCode status,
		string code)
	{
		_harness.Events("cal-3", Event("team", "2026-01-05T11:00:00.0000000", "2026-01-05T12:00:00.0000000"))
			.Server.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.CalendarViewPath("cal-1"),
				status,
				$$"""{"error":{"code":"{{code}}","message":"refused"} }""");

		var events = await ReadAsync(["cal-1", "cal-3"]);
		var issue = (await _harness.Integration.GetIssuesAsync()).Single();

		Assert.Multiple(() =>
		{
			Assert.That(events.Select(e => e.Id), Is.EqualTo(new[] { "team" }));
			Assert.That(issue.Severity, Is.EqualTo(IntegrationIssueSeverity.Warning));
			Assert.That(TestLocalization.Resolve(issue.Title), Does.Contain(Email));
			Assert.That(TestLocalization.Resolve(issue.Description), Does.Contain("Edit the connection"));
		});
	}

	[Test]
	public async Task A_chosen_calendar_missing_from_the_account_is_reported_until_it_is_back()
	{
		_harness.Calendars(MicrosoftCalendarHarness.Calendar("cal-1", "Calendar", isDefault: true));
		await _harness.Integration.GetCalendarsAsync(_account, CancellationToken.None);
		var whileMissing = await _harness.Integration.GetIssuesAsync();

		_harness.Calendars(MicrosoftCalendarHarness.Calendar("cal-1", "Calendar", isDefault: true),
			MicrosoftCalendarHarness.Calendar("cal-3", "Team", ownerAddress: "alex@contoso.com"));
		await _harness.Integration.GetCalendarsAsync(_account, CancellationToken.None);

		Assert.Multiple(async () =>
		{
			Assert.That(whileMissing.Select(i => i.Severity), Is.EqualTo(new[] { IntegrationIssueSeverity.Warning }));
			Assert.That(await _harness.Integration.GetIssuesAsync(), Is.Empty);
		});
	}

	[Test]
	public void Losing_access_to_every_calendar_fails_the_read()
	{
		_harness.Server.On(r => r.Uri.AbsolutePath.Contains("/calendarView", StringComparison.Ordinal),
			HttpStatusCode.Forbidden,
			"""{"error":{"code":"ErrorAccessDenied","message":"Access is denied."}}""");

		Assert.ThrowsAsync<HttpRequestException>(() => ReadAsync(["cal-1", "cal-3"]));
	}

	[Test]
	public async Task The_details_of_an_event_are_read_from_its_own_calendar_with_the_description()
	{
		_harness.Server.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.EventPath("cal-3", "ev"),
			HttpStatusCode.OK,
			Event("ev", "2026-01-05T09:00:00.0000000", "2026-01-05T10:00:00.0000000",
				extra: """ "body": { "contentType": "text", "content": "  Agenda: budget\n" } """));

		var details = await _harness.Integration.GetEventAsync(_account, "cal-3", "ev", CancellationToken.None);
		var request = _harness.Server.To(MicrosoftCalendarHarness.EventPath("cal-3", "ev")).Single();

		Assert.Multiple(() =>
		{
			Assert.That(details!.Description, Is.EqualTo("Agenda: budget"));
			Assert.That(request.Prefer, Does.Contain("outlook.body-content-type=\"text\""));
		});
	}

	[TestCase(HttpStatusCode.NotFound)]
	[TestCase(HttpStatusCode.Gone)]
	public async Task A_deleted_event_reads_as_gone(HttpStatusCode status)
	{
		_harness.Server.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.EventPath("cal-1", "ev"), status, "{}");

		Assert.That(await _harness.Integration.GetEventAsync(_account, "cal-1", "ev", CancellationToken.None), Is.Null);
	}

	[Test]
	public async Task An_event_of_a_calendar_that_was_not_chosen_is_not_read()
	{
		Assert.Multiple(async () =>
		{
			Assert.That(await _harness.Integration.GetEventAsync(_account, "cal-2", "ev", CancellationToken.None),
				Is.Null);
			Assert.That(_harness.Server.Requests, Is.Empty);
		});
	}

	private Task<IReadOnlyList<CalendarEvent>> ReadAsync(string[] calendarIds)
		=> _harness.Integration.GetEventsAsync(_account,
			new CalendarEventQuery { From = _from, To = _to, CalendarIds = calendarIds },
			CancellationToken.None);

	private static string Event(string id, string start, string end, string extra = "")
		=> $$"""
			{"id":"{{id}}","subject":"{{id}}",
			 "start":{"dateTime":"{{start}}","timeZone":"UTC"},"end":{"dateTime":"{{end}}","timeZone":"UTC"}
			 {{(extra.Length > 0 ? "," + extra : "")}} }
			""";
}
