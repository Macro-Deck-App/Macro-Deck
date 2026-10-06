using System.Globalization;
using System.Net;
using MacroDeck.Sdk.Calendar;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Integrations.GoogleCalendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.GoogleCalendar;

[TestFixture]
internal sealed class GoogleCalendarAccountTests
{
	private const string Email = "me@example.com";

	private GoogleCalendarHarness _harness = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new GoogleCalendarHarness();
		_harness.Calendars($$"""{"id":"{{Email}}","primary":true}""").Events(Email);
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task Each_connected_account_is_offered_under_its_entry()
	{
		var work = _harness.AddAccount("work@example.com", title: "Work");
		var home = _harness.AddAccount("home@example.com");
		await _harness.StartAsync();

		Assert.That(_harness.Integration.GetAccounts().Select(a => (a.Id, a.DisplayName)),
			Is.EqualTo(new[] { (work.ToString("D"), "Work"), (home.ToString("D"), "home@example.com") }));
	}

	[Test]
	public async Task A_token_close_to_expiry_is_refreshed_before_the_read_and_kept()
	{
		var entry = _harness.AddAccount(Email, expiresIn: TimeSpan.FromMinutes(4));
		_harness.Server.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.TokenPath,
			HttpStatusCode.OK,
			"""{"access_token":"access-1","expires_in":3600,"token_type":"Bearer"}""");
		await _harness.StartAsync();

		await ReadAsync(entry);

		var refresh = _harness.Server.To(GoogleCalendarHarness.TokenPath).Single().Form;
		Assert.Multiple(() =>
		{
			Assert.That(refresh["grant_type"], Is.EqualTo("refresh_token"));
			Assert.That(refresh["refresh_token"], Is.EqualTo("refresh-0"));
			Assert.That(refresh["client_id"], Is.EqualTo("client-id"));
			Assert.That(refresh["client_secret"], Is.EqualTo("client-secret"));
			Assert.That(_harness.Server.To("/calendar/").Select(r => r.Authorization), Is.All.EqualTo("Bearer access-1"));
			Assert.That(_harness.Config.Secrets[(entry, GoogleCalendarConfigKeys.AccessToken)], Is.EqualTo("access-1"));
			Assert.That(_harness.Config.Secrets[(entry, GoogleCalendarConfigKeys.RefreshToken)], Is.EqualTo("refresh-0"));
			Assert.That(DateTimeOffset.Parse(_harness.Config.Strings[(entry, GoogleCalendarConfigKeys.ExpiresAt)]!,
					CultureInfo.InvariantCulture),
				Is.EqualTo(_harness.Time.Now.AddHours(1)));
		});
	}

	[Test]
	public async Task A_token_with_time_left_is_used_as_it_is()
	{
		var entry = _harness.AddAccount(Email, expiresIn: TimeSpan.FromMinutes(10));
		await _harness.StartAsync();

		await ReadAsync(entry);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Server.To(GoogleCalendarHarness.TokenPath), Is.Empty);
			Assert.That(_harness.Server.To("/calendar/").Select(r => r.Authorization), Is.All.EqualTo("Bearer access-0"));
		});
	}

	[Test]
	public async Task A_refresh_token_google_rotates_is_stored()
	{
		var entry = _harness.AddAccount(Email, expiresIn: TimeSpan.Zero);
		_harness.Server.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.TokenPath,
			HttpStatusCode.OK,
			"""{"access_token":"access-1","refresh_token":"refresh-1","expires_in":3600}""");
		await _harness.StartAsync();

		await ReadAsync(entry);

		Assert.That(_harness.Config.Secrets[(entry, GoogleCalendarConfigKeys.RefreshToken)], Is.EqualTo("refresh-1"));
	}

	[Test]
	public async Task A_rejected_api_token_is_refreshed_once_and_the_request_repeated()
	{
		var entry = _harness.AddAccount(Email);
		_harness.Server
			.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.EventsPath(Email) && r.Authorization == "Bearer access-0",
				HttpStatusCode.Unauthorized,
				"{}")
			.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.TokenPath,
				HttpStatusCode.OK,
				"""{"access_token":"access-1","expires_in":3600}""");
		await _harness.StartAsync();

		Assert.That(async () => await ReadAsync(entry), Throws.Nothing);
		Assert.That(_harness.Server.To(GoogleCalendarHarness.TokenPath), Has.Count.EqualTo(1));
	}

	[Test]
	public async Task A_revoked_sign_in_fails_reads_and_raises_an_issue_that_reconnects_the_account()
	{
		var entry = _harness.AddAccount(Email, expiresIn: TimeSpan.FromMinutes(1));
		_harness.Server.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.TokenPath,
			HttpStatusCode.BadRequest,
			"""{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""");
		await _harness.StartAsync();

		Assert.That(async () => await ReadAsync(entry), Throws.Exception);
		var issue = (await _harness.Integration.GetIssuesAsync()).Single();
		var resolution = await _harness.Integration.ResolveIssueAsync(issue.Id);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(IntegrationIssueSeverity.Error));
			Assert.That(TestLocalization.Resolve(issue.Title), Does.Contain(Email));
			Assert.That(TestLocalization.Resolve(issue.Description), Does.Contain("Testing").And.Contain("7 days"));
			Assert.That(resolution.Success, Is.True);
			Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
			Assert.That(async () => await ReadAsync(entry), Throws.Exception, "it stays unusable until reconnected");
			Assert.That(_harness.Server.To(GoogleCalendarHarness.TokenPath), Has.Count.EqualTo(1),
				"a revoked refresh token is not tried again");
		});
	}

	[Test]
	public async Task A_google_outage_while_refreshing_is_no_reason_to_reconnect()
	{
		var entry = _harness.AddAccount(Email, expiresIn: TimeSpan.Zero);
		_harness.Server.On(r => r.Uri.AbsolutePath == GoogleCalendarHarness.TokenPath,
			HttpStatusCode.ServiceUnavailable,
			"{}");
		await _harness.StartAsync();

		Assert.That(async () => await ReadAsync(entry), Throws.Exception);
		Assert.That(await _harness.Integration.GetIssuesAsync(), Is.Empty);
	}

	[Test]
	public async Task The_newest_connection_of_an_account_wins_and_the_older_one_is_flagged()
	{
		var older = _harness.AddAccount(Email,
			connectedAt: _harness.Time.Now.AddDays(-3),
			title: "Old connection",
			accessToken: "old-access");
		var newer = _harness.AddAccount("Me@Example.com", connectedAt: _harness.Time.Now, title: "New connection");
		await _harness.StartAsync();

		var issue = (await _harness.Integration.GetIssuesAsync()).Single();
		Assert.Multiple(() =>
		{
			Assert.That(_harness.Integration.GetAccounts().Select(a => a.Id), Is.EqualTo(new[] { newer.ToString("D") }));
			Assert.That(issue.Severity, Is.EqualTo(IntegrationIssueSeverity.Warning));
			Assert.That(TestLocalization.Resolve(issue.Title), Does.Contain("twice"));
			Assert.That(TestLocalization.Resolve(issue.Description), Does.Contain("Old connection"));
			Assert.That(() => _harness.Integration.GetCalendarsAsync(older.ToString("D"), CancellationToken.None),
				Throws.InstanceOf<InvalidOperationException>());
		});
	}

	private Task<IReadOnlyList<CalendarEvent>> ReadAsync(Guid entry)
		=> _harness.Integration.GetEventsAsync(entry.ToString("D"),
			new CalendarEventQuery
			{
				From = _harness.Time.Now,
				To = _harness.Time.Now.AddDays(1),
				CalendarIds = [Email]
			},
			CancellationToken.None);
}
