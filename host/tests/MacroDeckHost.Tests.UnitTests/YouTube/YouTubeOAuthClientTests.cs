using System.Net;
using MacroDeckHost.Integrations.YouTube.Auth;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeOAuthClientTests
{
	private static readonly DateTimeOffset _now = new(2026, 10, 5, 12, 0, 0, TimeSpan.Zero);

	private YouTubeFakeHttpHandler _handler = null!;
	private YouTubeOAuthClient _client = null!;

	[SetUp]
	public void SetUp()
	{
		_handler = new YouTubeFakeHttpHandler();
		_client = new YouTubeOAuthClient(_handler, new YouTubeManualClock(_now));
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_handler.Dispose();
	}

	[Test]
	public async Task The_device_request_asks_google_for_the_youtube_scope_only()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""
			{"device_code":"4/4-GMMhmHCXhWEzkobqIHGG_EnNYYsAkukHspeYUk9E8","user_code":"GQVQ-JKEC",
			 "verification_url":"https://www.google.com/device","expires_in":1800,"interval":5}
			""");

		var device = await _client.RequestDeviceCodeAsync("client-id", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_handler.RequestedUris[0].ToString(), Is.EqualTo("https://oauth2.googleapis.com/device/code"));
			Assert.That(_handler.RequestBodies[0],
				Is.EqualTo("client_id=client-id&scope=" + Uri.EscapeDataString("https://www.googleapis.com/auth/youtube")));
			Assert.That(device.DeviceCode, Is.EqualTo("4/4-GMMhmHCXhWEzkobqIHGG_EnNYYsAkukHspeYUk9E8"));
			Assert.That(device.UserCode, Is.EqualTo("GQVQ-JKEC"));
			Assert.That(device.VerificationUrl, Is.EqualTo("https://www.google.com/device"));
			Assert.That(device.ExpiresIn, Is.EqualTo(TimeSpan.FromMinutes(30)));
			Assert.That(device.Interval, Is.EqualTo(TimeSpan.FromSeconds(5)));
		});
	}

	[Test]
	public void A_device_request_rate_limit_is_transient()
	{
		_handler.Enqueue(HttpStatusCode.Forbidden, """{"error_code":"rate_limit_exceeded","error":"rate_limit_exceeded"}""");

		Assert.ThrowsAsync<YouTubeOAuthTransientException>(() =>
			_client.RequestDeviceCodeAsync("client-id", CancellationToken.None));
	}

	[Test]
	public void A_device_request_for_an_unknown_client_is_rejected()
	{
		_handler.Enqueue(HttpStatusCode.Unauthorized,
			"""{"error":"invalid_client","error_description":"The OAuth client was not found."}""");

		var error = Assert.ThrowsAsync<YouTubeOAuthRejectedException>(() =>
			_client.RequestDeviceCodeAsync("client-id", CancellationToken.None));

		Assert.That(error!.ErrorCode, Is.EqualTo("invalid_client"));
	}

	[Test]
	public async Task A_granted_poll_sends_the_device_code_grant_with_the_client_secret()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""
			{"access_token":"ya29.at","expires_in":3599,"refresh_token":"1//rt",
			 "scope":"https://www.googleapis.com/auth/youtube","token_type":"Bearer"}
			""");

		var poll = await _client.PollTokenAsync("client-id", "secret", "device-code", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_handler.RequestedUris[0].ToString(), Is.EqualTo("https://oauth2.googleapis.com/token"));
			var form = _handler.RequestBodies[0];
			Assert.That(form, Does.Contain("client_id=client-id"));
			Assert.That(form, Does.Contain("client_secret=secret"));
			Assert.That(form, Does.Contain("device_code=device-code"));
			Assert.That(form,
				Does.Contain("grant_type=" + Uri.EscapeDataString("urn:ietf:params:oauth:grant-type:device_code")));
			Assert.That(poll.Status, Is.EqualTo(YouTubeTokenPollStatus.Success));
			Assert.That(poll.Tokens!.AccessToken, Is.EqualTo("ya29.at"));
			Assert.That(poll.Tokens.RefreshToken, Is.EqualTo("1//rt"));
			Assert.That(poll.Tokens.ExpiresAt, Is.EqualTo(_now.AddSeconds(3599)));
			Assert.That(poll.Tokens.Scopes, Is.EqualTo(new[] { "https://www.googleapis.com/auth/youtube" }));
		});
	}

	[TestCase(HttpStatusCode.PreconditionRequired, "authorization_pending", YouTubeTokenPollStatus.Pending)]
	[TestCase(HttpStatusCode.Forbidden, "slow_down", YouTubeTokenPollStatus.SlowDown)]
	[TestCase(HttpStatusCode.Forbidden, "access_denied", YouTubeTokenPollStatus.Denied)]
	[TestCase(HttpStatusCode.BadRequest, "expired_token", YouTubeTokenPollStatus.Expired)]
	[TestCase(HttpStatusCode.BadRequest, "invalid_grant", YouTubeTokenPollStatus.Expired)]
	[TestCase(HttpStatusCode.Unauthorized, "invalid_client", YouTubeTokenPollStatus.Rejected)]
	[TestCase(HttpStatusCode.BadRequest, "unauthorized_client", YouTubeTokenPollStatus.Rejected)]
	[TestCase(HttpStatusCode.Forbidden, "org_internal", YouTubeTokenPollStatus.Rejected)]
	[TestCase(HttpStatusCode.BadRequest, "admin_policy_enforced", YouTubeTokenPollStatus.Rejected)]
	public async Task A_documented_poll_error_is_classified(
		HttpStatusCode status,
		string code,
		YouTubeTokenPollStatus expected)
	{
		_handler.Enqueue(status, $$"""{"error":"{{code}}","error_description":"Bad Request"}""");

		var poll = await _client.PollTokenAsync("client-id", "secret", "device-code", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(poll.Status, Is.EqualTo(expected));
			Assert.That(poll.ErrorCode, Is.EqualTo(code));
			Assert.That(poll.Tokens, Is.Null);
		});
	}

	[TestCase(HttpStatusCode.InternalServerError)]
	[TestCase(HttpStatusCode.ServiceUnavailable)]
	[TestCase(HttpStatusCode.TooManyRequests)]
	public void A_server_side_poll_failure_is_transient(HttpStatusCode status)
	{
		_handler.Enqueue(status, string.Empty);

		Assert.ThrowsAsync<YouTubeOAuthTransientException>(() =>
			_client.PollTokenAsync("client-id", "secret", "device-code", CancellationToken.None));
	}

	[Test]
	public void An_unreachable_google_is_transient()
	{
		_handler.EnqueueFailure(new HttpRequestException("no route"));

		Assert.ThrowsAsync<YouTubeOAuthTransientException>(() =>
			_client.PollTokenAsync("client-id", "secret", "device-code", CancellationToken.None));
	}

	[Test]
	public async Task A_refresh_keeps_the_old_refresh_token_when_google_omits_it()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""
			{"access_token":"ya29.new","expires_in":3599,
			 "scope":"https://www.googleapis.com/auth/youtube","token_type":"Bearer"}
			""");

		var tokens = await _client.RefreshAsync("client-id", "secret", "1//old", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_handler.RequestBodies[0], Does.Contain("grant_type=refresh_token"));
			Assert.That(_handler.RequestBodies[0], Does.Contain("client_secret=secret"));
			Assert.That(_handler.RequestBodies[0], Does.Contain("refresh_token=1%2F%2Fold"));
			Assert.That(tokens.AccessToken, Is.EqualTo("ya29.new"));
			Assert.That(tokens.RefreshToken, Is.EqualTo("1//old"));
			Assert.That(tokens.ExpiresAt, Is.EqualTo(_now.AddSeconds(3599)));
		});
	}

	[Test]
	public async Task A_refresh_adopts_a_rotated_refresh_token()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""{"access_token":"ya29.new","expires_in":3599,"refresh_token":"1//new","token_type":"Bearer"}""");

		var tokens = await _client.RefreshAsync("client-id", "secret", "1//old", CancellationToken.None);

		Assert.That(tokens.RefreshToken, Is.EqualTo("1//new"));
	}

	[TestCase(HttpStatusCode.BadRequest, "invalid_grant")]
	[TestCase(HttpStatusCode.Unauthorized, "invalid_client")]
	public void A_dead_refresh_token_or_client_is_rejected(HttpStatusCode status, string code)
	{
		_handler.Enqueue(status, $$"""{"error":"{{code}}","error_description":"Token has been expired or revoked."}""");

		var error = Assert.ThrowsAsync<YouTubeOAuthRejectedException>(() =>
			_client.RefreshAsync("client-id", "secret", "1//old", CancellationToken.None));

		Assert.That(error!.ErrorCode, Is.EqualTo(code));
	}

	[TestCase(HttpStatusCode.InternalServerError)]
	[TestCase(HttpStatusCode.BadGateway)]
	[TestCase(HttpStatusCode.TooManyRequests)]
	public void A_server_side_refresh_failure_is_transient(HttpStatusCode status)
	{
		_handler.Enqueue(status, "<html>oops</html>");

		Assert.ThrowsAsync<YouTubeOAuthTransientException>(() =>
			_client.RefreshAsync("client-id", "secret", "1//old", CancellationToken.None));
	}

	[Test]
	public void An_unreachable_google_during_refresh_is_transient()
	{
		_handler.EnqueueFailure(new HttpRequestException("no route"));

		Assert.ThrowsAsync<YouTubeOAuthTransientException>(() =>
			_client.RefreshAsync("client-id", "secret", "1//old", CancellationToken.None));
	}
}
