using System.Net;
using MacroDeckHost.Integrations.Spotify.Auth;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyOAuthClientTests
{
	private const string RedirectUri = "http://127.0.0.1:44556/api/integrations/oauth/callback";

	[Test]
	public void A_code_exchange_that_hits_the_http_timeout_is_transient()
	{
		var client = Client(_ => throw new TaskCanceledException("HttpClient.Timeout", new TimeoutException()));

		Assert.That(async () => await Exchange(client), Throws.InstanceOf<SpotifyAuthTransientException>());
	}

	[Test]
	public void A_code_exchange_that_cannot_reach_spotify_is_transient()
	{
		var client = Client(_ => throw new HttpRequestException("No such host is known."));

		Assert.That(async () => await Exchange(client), Throws.InstanceOf<SpotifyAuthTransientException>());
	}

	[Test]
	public void A_code_exchange_answered_with_a_server_error_is_transient()
	{
		var client = Client(_ => Json(HttpStatusCode.ServiceUnavailable, """{"error":"server_error"}"""));

		Assert.That(async () => await Exchange(client), Throws.InstanceOf<SpotifyAuthTransientException>());
	}

	[Test]
	public void A_rate_limited_code_exchange_is_transient()
	{
		var client = Client(_ => Json((HttpStatusCode)429, """{"error":"rate_limited"}"""));

		Assert.That(async () => await Exchange(client),
			Throws.InstanceOf<SpotifyAuthTransientException>().With.Property("IsRateLimit").True);
	}

	[Test]
	public void A_code_exchange_with_wrong_app_credentials_is_rejected()
	{
		var client = Client(_ => Json(HttpStatusCode.BadRequest,
			"""{"error":"invalid_client","error_description":"Invalid client"}"""));

		Assert.That(async () => await Exchange(client), Throws.InstanceOf<SpotifyAuthRejectedException>());
	}

	[Test]
	public void A_code_exchange_cancelled_by_the_caller_stays_a_cancellation()
	{
		using var cancellation = new CancellationTokenSource();
		var client = Client(_ =>
		{
			cancellation.Cancel();
			throw new OperationCanceledException(cancellation.Token);
		});

		Assert.That(async () => await Exchange(client, cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
	}

	[Test]
	public void A_token_refresh_that_hits_the_http_timeout_is_still_transient()
	{
		var client = Client(_ => throw new TaskCanceledException("HttpClient.Timeout", new TimeoutException()));

		Assert.That(async () => await client.RefreshAsync("client-id", "client-secret", "refresh", CancellationToken.None),
			Throws.InstanceOf<SpotifyAuthTransientException>());
	}

	private static SpotifyOAuthClient Client(Func<IRequest, IResponse> handler)
	{
		return new SpotifyOAuthClient(new StubSpotifyHttpClient { Handler = handler });
	}

	private static Task<SpotifyAuthorizedToken> Exchange(
		SpotifyOAuthClient client,
		CancellationToken cancellationToken = default)
	{
		return client.ExchangeCodeAsync("client-id", "client-secret", "code", RedirectUri, cancellationToken);
	}

	private static Response Json(HttpStatusCode statusCode, string body)
	{
		return new Response(new Dictionary<string, string>())
		{
			StatusCode = statusCode,
			ContentType = "application/json",
			Body = body
		};
	}
}
