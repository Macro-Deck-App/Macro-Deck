using System.Globalization;
using System.Net;
using MacroDeck.Sdk.Calendar;
using MacroDeck.Sdk.Issues;
using MacroDeckHost.Integrations.MicrosoftCalendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.MicrosoftCalendar;

[TestFixture]
internal sealed class MicrosoftCalendarAccountTests
{
	private const string Email = "me@contoso.com";

	private MicrosoftCalendarHarness _harness = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new MicrosoftCalendarHarness();
		_harness.Calendars(MicrosoftCalendarHarness.Calendar("cal-1", "Calendar", isDefault: true)).Events("cal-1");
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task Several_accounts_are_offered_side_by_side_under_their_entries()
	{
		var work = _harness.AddAccount("megan@contoso.com", title: "Work");
		var home = _harness.AddAccount("megan@outlook.com");
		await _harness.StartAsync();

		Assert.That(_harness.Integration.GetAccounts().Select(a => (a.Id, a.DisplayName)),
			Is.EqualTo(new[] { (work.ToString("D"), "Work"), (home.ToString("D"), "megan@outlook.com") }));
	}

	[Test]
	public async Task A_token_close_to_expiry_is_refreshed_before_the_read_and_the_rotated_refresh_token_kept()
	{
		var entry = _harness.AddAccount(Email, expiresIn: TimeSpan.FromMinutes(4));
		_harness.Server.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.TokenPath,
			HttpStatusCode.OK,
			"""{"token_type":"Bearer","scope":"Calendars.Read openid profile email","expires_in":3599,"ext_expires_in":3599,"access_token":"access-1","refresh_token":"refresh-1"}""");
		await _harness.StartAsync();

		await ReadAsync(entry);

		var refresh = _harness.Server.To(MicrosoftCalendarHarness.TokenPath).Single().Form;
		Assert.Multiple(() =>
		{
			Assert.That(refresh["grant_type"], Is.EqualTo("refresh_token"));
			Assert.That(refresh["refresh_token"], Is.EqualTo("refresh-0"));
			Assert.That(refresh["client_id"], Is.EqualTo(MicrosoftCalendarHarness.ClientId));
			Assert.That(refresh, Does.Not.ContainKey("client_secret"), "a public client has no secret");
			Assert.That(refresh["scope"].Split(' '), Does.Contain("offline_access"));
			Assert.That(_harness.Server.To("/v1.0/").Select(r => r.Authorization), Is.All.EqualTo("Bearer access-1"));
			Assert.That(_harness.Config.Secrets[(entry, MicrosoftCalendarConfigKeys.AccessToken)], Is.EqualTo("access-1"));
			Assert.That(_harness.Config.Secrets[(entry, MicrosoftCalendarConfigKeys.RefreshToken)], Is.EqualTo("refresh-1"));
			Assert.That(_harness.Config.WriteOrder.IndexOf(MicrosoftCalendarConfigKeys.RefreshToken),
				Is.LessThan(_harness.Config.WriteOrder.IndexOf(MicrosoftCalendarConfigKeys.AccessToken)),
				"the rotated refresh token is stored first");
			Assert.That(DateTimeOffset.Parse(_harness.Config.Strings[(entry, MicrosoftCalendarConfigKeys.ExpiresAt)]!,
					CultureInfo.InvariantCulture),
				Is.EqualTo(_harness.Time.Now + TimeSpan.FromSeconds(3599)));
		});
	}

	[Test]
	public async Task An_account_connected_through_macro_decks_app_refreshes_with_that_app()
	{
		var entry = _harness.AddAccount(Email, expiresIn: TimeSpan.Zero);
		_harness.Config.Strings.Remove((entry, MicrosoftCalendarConfigKeys.ClientId));
		_harness.Server.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.TokenPath,
			HttpStatusCode.OK,
			"""{"token_type":"Bearer","expires_in":3599,"access_token":"access-1","refresh_token":"refresh-1"}""");
		await _harness.StartAsync();

		await ReadAsync(entry);

		Assert.That(_harness.Server.To(MicrosoftCalendarHarness.TokenPath).Single().Form["client_id"],
			Is.EqualTo(MicrosoftOAuth.DefaultClientId));
	}

	[Test]
	public async Task An_account_refreshes_with_the_app_and_tenant_it_signed_in_with()
	{
		var entry = _harness.AddAccount(Email, expiresIn: TimeSpan.Zero);
		_harness.Config.Strings[(entry, MicrosoftCalendarConfigKeys.ClientId)] = "";
		_harness.Config.Strings[(entry, MicrosoftCalendarConfigKeys.SignInClientId)] = "99999999-8888-7777-6666-555555555555";
		_harness.Config.Strings[(entry, MicrosoftCalendarConfigKeys.SignInTenant)] = "consumers";
		_harness.Server.On(r => r.Uri.AbsolutePath == "/consumers/oauth2/v2.0/token",
			HttpStatusCode.OK,
			"""{"token_type":"Bearer","expires_in":3599,"access_token":"access-1","refresh_token":"refresh-1"}""");
		await _harness.StartAsync();

		await ReadAsync(entry);

		Assert.That(_harness.Server.To("/consumers/oauth2/v2.0/token").Single().Form["client_id"],
			Is.EqualTo("99999999-8888-7777-6666-555555555555"));
	}

	[Test]
	public async Task A_token_with_time_left_is_used_as_it_is()
	{
		var entry = _harness.AddAccount(Email);
		await _harness.StartAsync();

		await ReadAsync(entry);

		Assert.That(_harness.Server.To(MicrosoftCalendarHarness.TokenPath), Is.Empty);
	}

	[Test]
	public async Task A_rejected_access_token_is_refreshed_once_and_the_request_repeated()
	{
		var entry = _harness.AddAccount(Email);
		_harness.Server
			.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.CalendarsPath && r.Authorization == "Bearer access-0",
				HttpStatusCode.Unauthorized,
				"""{"error":{"code":"InvalidAuthenticationToken","message":"Access token has expired."}}""")
			.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.TokenPath,
				HttpStatusCode.OK,
				"""{"token_type":"Bearer","expires_in":3599,"access_token":"access-1","refresh_token":"refresh-1"}""");
		await _harness.StartAsync();

		var calendars = await _harness.Integration.GetCalendarsAsync(entry.ToString("D"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(calendars.Select(c => c.Id), Is.EqualTo(new[] { "cal-1" }));
			Assert.That(_harness.Server.To(MicrosoftCalendarHarness.TokenPath), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_revoked_sign_in_fails_reads_and_raises_an_issue_that_reconnects_the_account()
	{
		var entry = _harness.AddAccount(Email, expiresIn: TimeSpan.Zero);
		_harness.Server.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.TokenPath,
			HttpStatusCode.BadRequest,
			"""{"error":"invalid_grant","error_description":"AADSTS70000: The provided grant has expired due to it being revoked.","error_codes":[70000]}""");
		await _harness.StartAsync();

		Assert.ThrowsAsync<MicrosoftOAuthRejectedException>(() => ReadAsync(entry));
		var issue = (await _harness.Integration.GetIssuesAsync()).Single();
		var resolution = await _harness.Integration.ResolveIssueAsync(issue.Id);

		Assert.Multiple(() =>
		{
			Assert.That(issue.Severity, Is.EqualTo(IntegrationIssueSeverity.Error));
			Assert.That(TestLocalization.Resolve(issue.Title), Does.Contain(Email));
			Assert.That(resolution.FollowUp, Is.EqualTo(IssueResolutionFollowUp.StartConfigFlow));
			Assert.ThrowsAsync<MicrosoftOAuthRejectedException>(() => ReadAsync(entry));
			Assert.That(_harness.Server.To(MicrosoftCalendarHarness.TokenPath), Has.Count.EqualTo(1),
				"a refused sign-in is not retried");
		});
	}

	[TestCase(HttpStatusCode.ServiceUnavailable, "{}")]
	[TestCase(HttpStatusCode.BadRequest,
		"""{"error":"temporarily_unavailable","error_description":"AADSTS90033: A transient error has occurred.","error_codes":[90033]}""")]
	public async Task A_microsoft_outage_while_refreshing_is_no_reason_to_reconnect(HttpStatusCode status, string body)
	{
		var entry = _harness.AddAccount(Email, expiresIn: TimeSpan.Zero);
		_harness.Server.On(r => r.Uri.AbsolutePath == MicrosoftCalendarHarness.TokenPath, status, body);
		await _harness.StartAsync();

		Assert.ThrowsAsync<MicrosoftOAuthTransientException>(() => ReadAsync(entry));
		Assert.That(await _harness.Integration.GetIssuesAsync(), Is.Empty);
	}

	[Test]
	public async Task The_newest_connection_of_an_account_wins_and_the_older_one_is_flagged()
	{
		_harness.AddAccount(Email, title: "Old connection", accountKey: "tid/oid",
			connectedAt: _harness.Time.Now - TimeSpan.FromDays(3));
		var newer = _harness.AddAccount(Email, title: "New connection", accountKey: "tid/oid");
		await _harness.StartAsync();

		var issue = (await _harness.Integration.GetIssuesAsync()).Single();

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Integration.GetAccounts().Select(a => a.Id), Is.EqualTo(new[] { newer.ToString("D") }));
			Assert.That(issue.Severity, Is.EqualTo(IntegrationIssueSeverity.Warning));
			Assert.That(TestLocalization.Resolve(issue.Description), Does.Contain("Old connection"));
		});
	}

	[Test]
	public void An_unknown_account_is_a_failure()
		=> Assert.ThrowsAsync<InvalidOperationException>(() => _harness.Integration.GetCalendarsAsync("nope",
			CancellationToken.None));

	private Task<IReadOnlyList<CalendarEvent>> ReadAsync(Guid entry)
		=> _harness.Integration.GetEventsAsync(entry.ToString("D"),
			new CalendarEventQuery
			{
				From = _harness.Time.Now,
				To = _harness.Time.Now + TimeSpan.FromDays(1)
			},
			CancellationToken.None);
}
