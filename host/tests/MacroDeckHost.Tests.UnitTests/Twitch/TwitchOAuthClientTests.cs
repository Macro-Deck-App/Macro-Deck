using System.Net;
using MacroDeckHost.Integrations.Twitch.Auth;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchOAuthClientTests
{
	private static readonly string[] _readChatScope = ["user:read:chat"];

	private FakeHttpMessageHandler _handler = null!;
	private TwitchOAuthClient _client = null!;

	[SetUp]
	public void SetUp()
	{
		_handler = new FakeHttpMessageHandler();
		_client = new TwitchOAuthClient(_handler);
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_handler.Dispose();
	}

	[Test]
	public async Task The_device_request_sends_the_scopes_parameter_twitch_expects()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""
			{"device_code":"dc","user_code":"ABCD-1234","verification_uri":"https://twitch.tv/activate",
			 "expires_in":1800,"interval":5}
			""");

		var device = await _client.RequestDeviceCodeAsync("client-id", "a b", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_handler.RequestedUris[0].ToString(), Is.EqualTo(TwitchOAuthEndpoints.Device));
			Assert.That(_handler.RequestBodies[0], Does.Contain("scopes=a+b"));
			Assert.That(_handler.RequestBodies[0], Does.Contain("client_id=client-id"));
			Assert.That(device.UserCode, Is.EqualTo("ABCD-1234"));
			Assert.That(device.Interval, Is.EqualTo(TimeSpan.FromSeconds(5)));
			Assert.That(device.ExpiresIn, Is.EqualTo(TimeSpan.FromMinutes(30)));
		});
	}

	[Test]
	public async Task The_token_request_uses_the_device_code_grant()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""
			{"access_token":"at","refresh_token":"rt","expires_in":14400,"scope":["user:read:chat"]}
			""");

		var result = await _client.PollTokenAsync("client-id", "a b", "dc", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Status, Is.EqualTo(TwitchTokenPollStatus.Success));
			Assert.That(result.Tokens!.AccessToken, Is.EqualTo("at"));
			Assert.That(result.Tokens.RefreshToken, Is.EqualTo("rt"));
			Assert.That(result.Tokens.Scopes, Is.EqualTo(_readChatScope));
			Assert.That(_handler.RequestBodies[0],
				Does.Contain(Uri.EscapeDataString(TwitchOAuthEndpoints.DeviceCodeGrantType)));
		});
	}

	[TestCase("authorization_pending", TwitchTokenPollStatus.Pending)]
	[TestCase("slow_down", TwitchTokenPollStatus.SlowDown)]
	[TestCase("expired_token", TwitchTokenPollStatus.Expired)]
	[TestCase("invalid device code", TwitchTokenPollStatus.Expired)]
	[TestCase("access_denied", TwitchTokenPollStatus.Denied)]
	public async Task A_poll_error_in_the_message_field_is_classified(string message, TwitchTokenPollStatus expected)
	{
		_handler.Enqueue(HttpStatusCode.BadRequest, $$"""{"status":400,"message":"{{message}}"}""");

		var result = await _client.PollTokenAsync("client-id", "a", "dc", CancellationToken.None);

		Assert.That(result.Status, Is.EqualTo(expected));
	}

	[Test]
	public async Task A_poll_error_in_the_rfc_error_field_is_classified_too()
	{
		_handler.Enqueue(HttpStatusCode.BadRequest, """{"error":"slow_down"}""");

		var result = await _client.PollTokenAsync("client-id", "a", "dc", CancellationToken.None);

		Assert.That(result.Status, Is.EqualTo(TwitchTokenPollStatus.SlowDown));
	}

	[Test]
	public async Task A_server_error_while_polling_only_slows_down()
	{
		_handler.Enqueue(HttpStatusCode.InternalServerError, string.Empty);

		var result = await _client.PollTokenAsync("client-id", "a", "dc", CancellationToken.None);

		Assert.That(result.Status, Is.EqualTo(TwitchTokenPollStatus.SlowDown));
	}

	[Test]
	public async Task A_refresh_sends_no_client_secret()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""{"access_token":"at2","refresh_token":"rt2","expires_in":14400,"scope":[]}""");

		var tokens = await _client.RefreshAsync("client-id", "rt", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_handler.RequestBodies[0], Does.Not.Contain("client_secret"));
			Assert.That(_handler.RequestBodies[0], Does.Contain("grant_type=refresh_token"));
			Assert.That(tokens.RefreshToken, Is.EqualTo("rt2"));
			Assert.That(tokens.ExpiresAt, Is.GreaterThan(DateTimeOffset.UtcNow.AddHours(3)));
		});
	}

	[Test]
	public void A_spent_refresh_token_is_terminal()
	{
		_handler.Enqueue(HttpStatusCode.Unauthorized,
			"""{"error":"Bad Request","status":400,"message":"Invalid refresh token"}""");

		Assert.ThrowsAsync<TwitchOAuthRejectedException>(() =>
			_client.RefreshAsync("client-id", "rt", CancellationToken.None));
	}

	[TestCase(HttpStatusCode.InternalServerError)]
	[TestCase(HttpStatusCode.ServiceUnavailable)]
	[TestCase(HttpStatusCode.TooManyRequests)]
	public void A_server_side_refresh_failure_is_transient(HttpStatusCode status)
	{
		_handler.Enqueue(status, string.Empty);

		Assert.ThrowsAsync<TwitchOAuthTransientException>(() =>
			_client.RefreshAsync("client-id", "rt", CancellationToken.None));
	}

	[Test]
	public async Task Validation_uses_the_oauth_authorization_scheme()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""
			{"client_id":"cid","login":"streamer","scopes":["user:read:chat"],"user_id":"12345","expires_in":14400}
			""");

		var identity = await _client.ValidateAsync("at", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_handler.AuthorizationHeaders[0], Is.EqualTo("OAuth at"));
			Assert.That(identity.UserId, Is.EqualTo("12345"));
			Assert.That(identity.Login, Is.EqualTo("streamer"));
			Assert.That(identity.Scopes, Is.EqualTo(_readChatScope));
		});
	}

	[Test]
	public void A_revoked_token_fails_validation_terminally()
	{
		_handler.Enqueue(HttpStatusCode.Unauthorized, """{"status":401,"message":"invalid access token"}""");

		Assert.ThrowsAsync<TwitchOAuthRejectedException>(() => _client.ValidateAsync("at", CancellationToken.None));
	}

	[Test]
	public void An_unreadable_response_is_transient()
	{
		_handler.Enqueue(HttpStatusCode.OK, "<html>gateway</html>");

		Assert.ThrowsAsync<TwitchOAuthTransientException>(() =>
			_client.RequestDeviceCodeAsync("client-id", "a", CancellationToken.None));
	}
}
