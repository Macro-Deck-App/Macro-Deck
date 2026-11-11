using System.Net;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.Discord.Auth;
using MacroDeckHost.Integrations.Discord.Configuration;
using MacroDeckHost.Integrations.GoogleCalendar.Auth;
using MacroDeckHost.Integrations.GoogleCalendar.Configuration;
using MacroDeckHost.Integrations.MicrosoftCalendar.Auth;
using MacroDeckHost.Integrations.MicrosoftCalendar.Configuration;
using MacroDeckHost.Integrations.MicrosoftCalendar.Protocol;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Integrations.Twitch.Configuration;
using MacroDeckHost.Integrations.YouTube.Auth;
using MacroDeckHost.Integrations.YouTube.Configuration;
using MacroDeckHost.Tests.UnitTests.Discord;
using MacroDeckHost.Tests.UnitTests.GoogleCalendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Twitch;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class TokenEndpointFaultTests
{
	private const string InvalidGrant =
		"""{"error":"invalid_grant","error_description":"Token has been expired or revoked."}""";

	private const string TwitchDeviceCode =
		"""
		{"device_code":"dc","user_code":"ABCD-1234","verification_uri":"https://twitch.tv/activate",
		 "expires_in":1800,"interval":5}
		""";

	private const string GoogleDeviceCode =
		"""
		{"device_code":"dc","user_code":"GQVQ-JKEC","verification_url":"https://www.google.com/device",
		 "expires_in":1800,"interval":5}
		""";

	private static readonly Guid _entryId = Guid.Parse("44444444-4444-4444-4444-444444444444");

	private static readonly Func<TimeSpan, CancellationToken, Task> _noDelay = (_, _) => Task.CompletedTask;

	private static readonly Action<FakeTokenEndpoint> _hang = endpoint => endpoint.TimesOut();

	private static readonly Action<FakeTokenEndpoint> _rateLimited = endpoint =>
		endpoint.Answers(HttpStatusCode.TooManyRequests, """{"error":"rate_limited"}""", TimeSpan.FromSeconds(30));

	private static readonly Action<FakeTokenEndpoint> _unavailable = endpoint =>
		endpoint.Answers(HttpStatusCode.ServiceUnavailable, "<html>down</html>");

	private static IEnumerable<TestCaseData> TransientFaults()
	{
		yield return Fault("hang", _hang);
		yield return Fault("429 with Retry-After", _rateLimited);
	}

	private static IEnumerable<TestCaseData> FlowFaults()
	{
		yield return Fault("hang", _hang);
		yield return Fault("429 with Retry-After", _rateLimited);
		yield return Fault("503", _unavailable);
	}

	[TestCaseSource(nameof(TransientFaults))]
	public void A_twitch_refresh_fault_is_transient_and_keeps_the_account(Action<FakeTokenEndpoint> fault)
	{
		using var endpoint = Endpoint(fault);
		using var client = new TwitchOAuthClient(endpoint);
		using var persister = new TwitchTokenPersister(new RecordingIntegrationConfig(), SilentLogger());
		using var provider = new TwitchTokenProvider(_entryId,
			"client-id",
			new TwitchTokens("stale", "refresh-0", DateTimeOffset.UtcNow.AddMinutes(1), TwitchScopes.All),
			client,
			persister,
			SilentLogger());

		Assert.ThrowsAsync<TwitchOAuthTransientException>(() => provider.GetAsync(CancellationToken.None));
		Assert.That(provider.NeedsReauthorization, Is.False);
	}

	[TestCaseSource(nameof(TransientFaults))]
	public void A_youtube_refresh_fault_is_transient_and_keeps_the_account(Action<FakeTokenEndpoint> fault)
	{
		using var endpoint = Endpoint(fault);
		using var client = new YouTubeOAuthClient(endpoint);
		using var persister = new YouTubeTokenPersister(new RecordingIntegrationConfig(), SilentLogger());
		using var provider = YouTubeProvider(client, persister);

		Assert.ThrowsAsync<YouTubeOAuthTransientException>(() => provider.GetAsync(CancellationToken.None));
		Assert.That(provider.NeedsReauthorization, Is.False);
	}

	[TestCaseSource(nameof(TransientFaults))]
	public void A_google_refresh_fault_is_transient_and_keeps_the_account(Action<FakeTokenEndpoint> fault)
	{
		using var endpoint = Endpoint(fault);
		using var client = new GoogleOAuthClient(endpoint);
		using var provider = new GoogleTokenProvider(_entryId,
			"client-id",
			"client-secret",
			new GoogleTokens("stale", "refresh-0", DateTimeOffset.UtcNow.AddMinutes(1)),
			client,
			(_, _) => Task.CompletedTask,
			TimeProvider.System,
			SilentLogger());

		Assert.ThrowsAsync<GoogleOAuthTransientException>(() => provider.GetAsync(CancellationToken.None));
		Assert.That(provider.NeedsReauthorization, Is.False);
	}

	[TestCaseSource(nameof(TransientFaults))]
	public void A_microsoft_refresh_fault_is_transient_and_keeps_the_account(Action<FakeTokenEndpoint> fault)
	{
		using var endpoint = Endpoint(fault);
		using var client = new MicrosoftOAuthClient(endpoint);
		using var provider = new MicrosoftTokenProvider(_entryId,
			"client-id",
			"common",
			new MicrosoftTokens("stale", "refresh-0", DateTimeOffset.UtcNow.AddMinutes(1)),
			client,
			(_, _) => Task.CompletedTask,
			TimeProvider.System,
			SilentLogger());

		Assert.ThrowsAsync<MicrosoftOAuthTransientException>(() => provider.GetAsync(CancellationToken.None));
		Assert.That(provider.NeedsReauthorization, Is.False);
	}

	[Test]
	public void A_revoked_youtube_refresh_token_is_sent_to_google_only_once()
	{
		using var endpoint = new FakeTokenEndpoint().Answers(HttpStatusCode.BadRequest, InvalidGrant);
		using var client = new YouTubeOAuthClient(endpoint);
		using var persister = new YouTubeTokenPersister(new RecordingIntegrationConfig(), SilentLogger());
		using var provider = YouTubeProvider(client, persister);

		Assert.ThrowsAsync<YouTubeOAuthRejectedException>(() => provider.GetAsync(CancellationToken.None));
		Assert.ThrowsAsync<YouTubeOAuthRejectedException>(() => provider.GetAsync(CancellationToken.None));
		Assert.ThrowsAsync<YouTubeOAuthRejectedException>(() => provider.ForceRefreshAsync(CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(provider.NeedsReauthorization, Is.True);
			Assert.That(endpoint.RequestCount, Is.EqualTo(1));
		});
	}

	[TestCaseSource(nameof(FlowFaults))]
	public async Task A_twitch_setup_answers_a_token_endpoint_fault_with_a_step(Action<FakeTokenEndpoint> fault)
	{
		using var endpoint = Endpoint(fault, HttpStatusCode.OK, TwitchDeviceCode);
		var flow = new TwitchConfigFlow(() => new TwitchOAuthClient(endpoint), _noDelay);
		await flow.StartAsync(NoOAuthConfigFlowContext.Instance, CancellationToken.None);

		var result = await flow.SubmitAsync("link",
			new Dictionary<string, object?>(),
			NoOAuthConfigFlowContext.Instance,
			CancellationToken.None);

		AssertRetryable(result);
	}

	[TestCaseSource(nameof(FlowFaults))]
	public async Task A_youtube_setup_answers_a_token_endpoint_fault_with_a_step(Action<FakeTokenEndpoint> fault)
	{
		using var endpoint = Endpoint(fault, HttpStatusCode.OK, GoogleDeviceCode);
		var flow = new YouTubeConfigFlow(() => new YouTubeOAuthClient(endpoint),
			(_, _, _) => throw new NotSupportedException(),
			_noDelay,
			TimeProvider.System);
		await flow.SubmitAsync("client",
			new Dictionary<string, object?>
			{
				[YouTubeConfigKeys.ClientId] = "client-id",
				[YouTubeConfigKeys.ClientSecret] = "client-secret"
			},
			NoOAuthConfigFlowContext.Instance,
			CancellationToken.None);

		var result = await flow.SubmitAsync("link",
			new Dictionary<string, object?>(),
			NoOAuthConfigFlowContext.Instance,
			CancellationToken.None);

		AssertRetryable(result);
	}

	[TestCaseSource(nameof(FlowFaults))]
	public async Task A_google_calendar_sign_in_answers_a_token_endpoint_fault_with_an_error(
		Action<FakeTokenEndpoint> fault)
	{
		using var endpoint = Endpoint(fault);
		using var client = new GoogleOAuthClient(endpoint);
		var flow = new GoogleCalendarConfigFlow(client, TimeProvider.System);
		var context = new FakeConfigFlowContext();
		await flow.SubmitAsync("credentials",
			new Dictionary<string, object?> { ["clientId"] = "client-id", ["clientSecret"] = "client-secret" },
			context,
			CancellationToken.None);
		context.AuthorizationCode = "auth-code";

		var result = await flow.SubmitAsync("authorize",
			new Dictionary<string, object?>(),
			context,
			CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	[TestCaseSource(nameof(FlowFaults))]
	public async Task A_microsoft_calendar_sign_in_answers_a_token_endpoint_fault_with_an_error(
		Action<FakeTokenEndpoint> fault)
	{
		using var endpoint = Endpoint(fault);
		using var client = new MicrosoftOAuthClient(endpoint);
		var flow = new MicrosoftCalendarConfigFlow(client,
			new MicrosoftGraphClient(endpoint, TimeProvider.System),
			_ => null,
			TimeProvider.System);
		var context = new FakeConfigFlowContext();
		await flow.SubmitAsync("app", new Dictionary<string, object?>(), context, CancellationToken.None);
		context.AuthorizationCode = "auth-code";

		var result = await flow.SubmitAsync("authorize",
			new Dictionary<string, object?>(),
			context,
			CancellationToken.None);

		Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
	}

	[TestCaseSource(nameof(FlowFaults))]
	public async Task A_discord_authorization_answers_a_token_endpoint_fault_with_an_error(
		Action<FakeTokenEndpoint> fault)
	{
		using var endpoint = Endpoint(fault);
		using var http = new HttpClient(endpoint, false);
		using var rpc = DiscordRpc();
		var flow = await DiscordFlowAtAuthorizeAsync(rpc, http);

		var result = await flow.SubmitAsync("authorize",
			new Dictionary<string, object?>(),
			NoOAuthConfigFlowContext.Instance,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep?.StepId, Is.EqualTo("authorize"), "the user can try again");
		});
	}

	[Test]
	public async Task A_cancelled_discord_authorization_is_not_reported_as_a_token_endpoint_fault()
	{
		using var endpoint = new FakeTokenEndpoint().Hangs();
		using var http = new HttpClient(endpoint, false);
		using var rpc = DiscordRpc();
		var flow = await DiscordFlowAtAuthorizeAsync(rpc, http);
		using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

		Assert.CatchAsync<OperationCanceledException>(() => flow.SubmitAsync("authorize",
			new Dictionary<string, object?>(),
			NoOAuthConfigFlowContext.Instance,
			cancellation.Token));
	}

	private static FakeDiscordRpcClient DiscordRpc()
	{
		return new FakeDiscordRpcClient()
			.Responds("AUTHORIZE", """{"code":"auth-code"}""")
			.Responds("AUTHENTICATE", """{"user":{"id":"me","global_name":"Tester"}}""");
	}

	private static async Task<DiscordConfigFlow> DiscordFlowAtAuthorizeAsync(FakeDiscordRpcClient rpc, HttpClient http)
	{
		var flow = new DiscordConfigFlow(() => rpc, new DiscordOAuthClient(http));
		await flow.SubmitAsync("credentials",
			new Dictionary<string, object?> { ["clientId"] = "123456", ["clientSecret"] = "shh" },
			NoOAuthConfigFlowContext.Instance,
			CancellationToken.None);
		return flow;
	}

	private static TestCaseData Fault(string name, Action<FakeTokenEndpoint> fault)
	{
		return new TestCaseData(fault).SetArgDisplayNames(name);
	}

	private static FakeTokenEndpoint Endpoint(Action<FakeTokenEndpoint> fault)
	{
		var endpoint = new FakeTokenEndpoint();
		fault(endpoint);
		return endpoint;
	}

	private static FakeTokenEndpoint Endpoint(Action<FakeTokenEndpoint> fault, HttpStatusCode status, string first)
	{
		var endpoint = new FakeTokenEndpoint().Answers(status, first);
		fault(endpoint);
		return endpoint;
	}

	private static void AssertRetryable(ConfigFlowResult result)
	{
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.AnyOf(ConfigFlowResultKind.Error, ConfigFlowResultKind.Step));
			Assert.That(result.NextStep?.StepId, Is.EqualTo("link"), "the user can try again from the same code");
		});
	}

	private static YouTubeTokenProvider YouTubeProvider(YouTubeOAuthClient client, YouTubeTokenPersister persister)
	{
		return new YouTubeTokenProvider(_entryId,
			"client-id",
			"client-secret",
			new YouTubeTokens("stale",
				"refresh-0",
				DateTimeOffset.UtcNow.AddMinutes(1),
				["https://www.googleapis.com/auth/youtube"]),
			client,
			persister,
			SilentLogger());
	}

	private static Logger SilentLogger()
	{
		return new LoggerConfiguration().CreateLogger();
	}
}
