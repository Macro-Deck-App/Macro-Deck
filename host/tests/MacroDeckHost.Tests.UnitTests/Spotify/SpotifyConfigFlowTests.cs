using System.Net;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.Spotify.Auth;
using MacroDeckHost.Integrations.Spotify.Configuration;
using MacroDeckHost.Integrations.Spotify.Protocol;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using SpotifyAPI.Web.Http;

namespace MacroDeckHost.Tests.UnitTests.Spotify;

[TestFixture]
internal sealed class SpotifyConfigFlowTests
{
	private const string RedirectUri = "http://127.0.0.1:44556/api/integrations/oauth/callback";

	[Test]
	public async Task The_credentials_step_shows_the_redirect_uri_as_a_copy_value_not_in_prose()
	{
		var flow = new SpotifyConfigFlow();
		var context = new StubContext(new StubOAuthSession(RedirectUri));

		var result = await flow.StartAsync(context, CancellationToken.None);

		var step = result.NextStep!;
		Assert.Multiple(() =>
		{
			Assert.That(step.Instructions, Has.Count.EqualTo(3));
			Assert.That(step.Instructions[1].Values, Has.Count.EqualTo(1));
			Assert.That(TestLocalization.Resolve(step.Instructions[1].Values[0].Label), Is.EqualTo("Redirect URI"));
			Assert.That(step.Instructions[1].Values[0].Value, Is.EqualTo(context.OAuth.RedirectUri));
			Assert.That(TestLocalization.Resolve(step.Description), Does.Not.Contain(context.OAuth.RedirectUri));
		});
	}

	[Test]
	public async Task The_credentials_step_keeps_the_developer_dashboard_link()
	{
		var flow = new SpotifyConfigFlow();
		var context = new StubContext(new StubOAuthSession(RedirectUri));

		var result = await flow.StartAsync(context, CancellationToken.None);

		var link = result.NextStep!.Links.Single();
		Assert.Multiple(() =>
		{
			Assert.That(TestLocalization.Resolve(link.Label), Is.EqualTo("Open Spotify Developer Dashboard"));
			Assert.That(link.Url, Is.EqualTo("https://developer.spotify.com/dashboard"));
		});
	}

	[Test]
	public async Task A_token_endpoint_that_times_out_returns_a_retryable_error_instead_of_throwing()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Exchanges.Enqueue(_ => throw new SpotifyAuthTransientException("The Spotify token request timed out.",
			new TaskCanceledException("HttpClient.Timeout", new TimeoutException())));
		var (flow, context) = await SignInUntilAuthorize(oauth, Profile());

		var result = await flow.SubmitAsync("authorize", Empty, context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("credentials"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Integrations.Spotify.Config.SpotifyUnreachable())));
		});
	}

	[Test]
	public async Task Rejected_credentials_still_report_the_token_exchange_failure()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Exchanges.Enqueue(_ => throw new SpotifyAuthRejectedException("invalid_client"));
		var (flow, context) = await SignInUntilAuthorize(oauth, Profile());

		var result = await flow.SubmitAsync("authorize", Empty, context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("authorize"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Integrations.Spotify.Config.TokenExchangeFailed())));
		});
	}

	[Test]
	public async Task A_token_endpoint_answer_that_is_not_json_returns_a_retryable_error_instead_of_throwing()
	{
		var tokenEndpoint = new StubSpotifyHttpClient
		{
			Handler = _ => new Response(new Dictionary<string, string>())
			{
				StatusCode = HttpStatusCode.OK,
				ContentType = "text/html",
				Body = "<html><body>Sign in to the network</body></html>"
			}
		};
		var (flow, context) = await SignInUntilAuthorize(new SpotifyOAuthClient(tokenEndpoint), Profile());

		var result = await flow.SubmitAsync("authorize", Empty, context, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage),
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Integrations.Spotify.Config.SpotifyUnreachable())));
		});
	}

	[Test]
	public async Task A_cancelled_submit_during_the_token_exchange_is_not_turned_into_an_error()
	{
		using var cancellation = new CancellationTokenSource();
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Exchanges.Enqueue(token =>
		{
			cancellation.Cancel();
			throw new OperationCanceledException(token);
		});
		var (flow, context) = await SignInUntilAuthorize(oauth, Profile());

		Assert.That(async () => await flow.SubmitAsync("authorize", Empty, context, cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
	}

	[Test]
	public async Task A_cancelled_submit_during_the_profile_lookup_does_not_complete_the_flow()
	{
		using var cancellation = new CancellationTokenSource();
		var profile = new StubSpotifyHttpClient
		{
			Handler = _ =>
			{
				cancellation.Cancel();
				throw new OperationCanceledException(cancellation.Token);
			}
		};
		var (flow, context) = await SignInUntilAuthorize(new FakeSpotifyOAuthClient(), profile);

		Assert.That(async () => await flow.SubmitAsync("authorize", Empty, context, cancellation.Token),
			Throws.InstanceOf<OperationCanceledException>());
	}

	[Test]
	public async Task After_a_timeout_continuing_signs_in_again_with_a_fresh_code_and_completes()
	{
		var oauth = new FakeSpotifyOAuthClient();
		oauth.Exchanges.Enqueue(_ => throw new SpotifyAuthTransientException("The Spotify token request timed out."));
		var createdAt = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
		oauth.Exchanges.Enqueue(_ => Task.FromResult(new SpotifyAuthorizedToken("fresh-access",
			"fresh-refresh",
			3600,
			createdAt,
			"user-read-playback-state")));
		var (flow, context) = await SignInUntilAuthorize(oauth, Profile("Ada"));
		var session = (StubOAuthSession)context.OAuth;

		var failed = await flow.SubmitAsync("authorize", Empty, context, CancellationToken.None);
		var relogin = await flow.SubmitAsync("credentials", Credentials, context, CancellationToken.None);
		session.AuthorizationCode = "second-code";
		var completed = await flow.SubmitAsync("authorize", Empty, context, CancellationToken.None);

		var values = completed.Values!;
		Assert.Multiple(() =>
		{
			Assert.That(failed.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(relogin.Kind, Is.EqualTo(ConfigFlowResultKind.External));
			Assert.That(relogin.ExternalUrl, Does.Contain($"state={session.State}"));
			Assert.That(relogin.ResumeStepId, Is.EqualTo("authorize"));
			Assert.That(oauth.ExchangedCodes, Is.EqualTo(new[] { "first-code", "second-code" }));
			Assert.That(completed.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(completed.EntryTitle, Is.EqualTo("Spotify (Ada)"));
			Assert.That(values[SpotifyConfigKeys.AccessToken], Is.EqualTo(ConfigFlowValue.Secret("fresh-access")));
			Assert.That(values[SpotifyConfigKeys.RefreshToken], Is.EqualTo(ConfigFlowValue.Secret("fresh-refresh")));
			Assert.That(values[SpotifyConfigKeys.ExpiresAt],
				Is.EqualTo(ConfigFlowValue.Plain("2026-10-16T13:00:00.0000000Z")));
			Assert.That(values[SpotifyConfigKeys.Scope], Is.EqualTo(ConfigFlowValue.Plain("user-read-playback-state")));
			Assert.That(values[SpotifyConfigKeys.DisplayName], Is.EqualTo(ConfigFlowValue.Plain("Ada")));
		});
	}

	private static IReadOnlyDictionary<string, object?> Empty { get; } = new Dictionary<string, object?>();

	private static IReadOnlyDictionary<string, object?> Credentials { get; } = new Dictionary<string, object?>
	{
		[SpotifyConfigKeys.ClientId] = "client-id",
		[SpotifyConfigKeys.ClientSecret] = "client-secret"
	};

	private static async Task<(SpotifyConfigFlow Flow, StubContext Context)> SignInUntilAuthorize(
		ISpotifyOAuthClient oauth,
		IHTTPClient profile)
	{
		var flow = new SpotifyConfigFlow(oauth, profile, new SpotifyRequestLimiter(1000, 1000));
		var context = new StubContext(new StubOAuthSession(RedirectUri) { AuthorizationCode = "first-code" });
		await flow.StartAsync(context, CancellationToken.None);
		await flow.SubmitAsync("credentials", Credentials, context, CancellationToken.None);
		return (flow, context);
	}

	private static StubSpotifyHttpClient Profile(string displayName = "Listener")
	{
		return new StubSpotifyHttpClient
		{
			Handler = _ => new Response(new Dictionary<string, string>())
			{
				StatusCode = HttpStatusCode.OK,
				ContentType = "application/json",
				Body = $$"""{"id":"listener-id","display_name":"{{displayName}}"}"""
			}
		};
	}

	private sealed class StubContext(IOAuthSession oauth) : IConfigFlowContext
	{
		public IOAuthSession OAuth { get; } = oauth;
	}

	private sealed class StubOAuthSession(string redirectUri) : IOAuthSession
	{
		public string RedirectUri => redirectUri;

		public string State => "flow-state";

		public string? AuthorizationCode { get; set; }
	}
}
