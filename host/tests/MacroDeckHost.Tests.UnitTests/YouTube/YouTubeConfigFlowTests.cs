using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Auth;
using MacroDeckHost.Integrations.YouTube.Protocol;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeConfigFlowTests
{
	private const string ClientId = "123-abc.apps.googleusercontent.com";
	private const string ClientSecret = "GOCSPX-secret";

	private FakeYouTubeOAuthClient _oauth = null!;
	private FakeYouTubeApiClient _api = null!;
	private List<TimeSpan> _delays = null!;
	private List<(string ClientId, int Quota, string Token)> _apiRequests = null!;
	private YouTubeConfigFlow _flow = null!;

	[SetUp]
	public void SetUp()
	{
		_oauth = new FakeYouTubeOAuthClient();
		_api = new FakeYouTubeApiClient { Channel = new YouTubeChannel("UCabc", "My Channel", "@mychannel", 99) };
		_delays = [];
		_apiRequests = [];
		_flow = new YouTubeConfigFlow(() => _oauth,
			(clientId, quota, token) =>
			{
				_apiRequests.Add((clientId, quota, token));
				return _api;
			},
			(delay, _) =>
			{
				_delays.Add(delay);
				return Task.CompletedTask;
			},
			new YouTubeManualClock(YouTubeTestSupport.Now));
	}

	[TearDown]
	public void TearDown() => _oauth.Dispose();

	[Test]
	public async Task Setup_starts_by_asking_for_the_google_client_and_says_where_to_create_it()
	{
		var step = (await _flow.StartAsync(null!, CancellationToken.None)).NextStep!;

		Assert.Multiple(() =>
		{
			Assert.That(step.StepId, Is.EqualTo("client"));
			Assert.That(step.Fields.Select(field => (field.Name, field.Type, field.Required)),
				Is.EqualTo(new[]
				{
					(YouTubeConfigKeys.ClientId, ActionParameterType.String, true),
					(YouTubeConfigKeys.ClientSecret, ActionParameterType.Secret, true)
				}));
			Assert.That(step.AdvancedFields.Single().Name, Is.EqualTo(YouTubeConfigKeys.DailyQuota));
			Assert.That(step.AdvancedFields.Single().DefaultValue, Is.EqualTo(10_000));
			Assert.That(step.Links.Select(link => link.Url), Is.EquivalentTo(new[]
			{
				"https://console.cloud.google.com/apis/credentials",
				"https://console.cloud.google.com/apis/library/youtube.googleapis.com",
				"https://docs.macro-deck.app/guide/youtube/"
			}));
			Assert.That(step.Instructions.Select(i => TestLocalization.Resolve(i.Text)),
				Has.Some.Contains("TVs and Limited Input devices"));
			Assert.That(_oauth.Calls, Is.Empty, "nothing is sent to Google before the client is known");
		});
	}

	[Test]
	public async Task The_client_id_and_the_secret_are_both_required()
	{
		await _flow.StartAsync(null!, CancellationToken.None);

		var result = await _flow.SubmitAsync("client",
			new Dictionary<string, object?> { [YouTubeConfigKeys.ClientId] = "  " },
			null!,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("client"));
			Assert.That(result.FieldErrors!.Keys,
				Is.EquivalentTo(new[] { YouTubeConfigKeys.ClientId, YouTubeConfigKeys.ClientSecret }));
			Assert.That(_oauth.Calls, Is.Empty);
		});
	}

	[TestCase(0)]
	[TestCase(-5)]
	[TestCase(2.5)]
	public async Task A_daily_quota_below_one_or_with_a_fraction_is_refused(double quota)
	{
		var result = await SubmitClient(quota);

		Assert.That(result.FieldErrors!.Keys, Is.EqualTo(new[] { YouTubeConfigKeys.DailyQuota }));
	}

	[Test]
	public async Task A_valid_client_leads_to_the_device_code_step()
	{
		var result = await SubmitClient();

		var step = result.NextStep!;
		var code = step.Instructions.SelectMany(instruction => instruction.Values).Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(step.StepId, Is.EqualTo("link"));
			Assert.That(code.Value, Is.EqualTo("ABCD-EFGH"));
			Assert.That(step.Links.Single().Url, Is.EqualTo("https://www.google.com/device"));
			Assert.That(TestLocalization.Resolve(step.Description), Does.Contain("30 minutes"));
			Assert.That(_oauth.Calls, Is.EqualTo(new[] { "device:" + ClientId }));
		});
	}

	[Test]
	public async Task A_client_google_rejects_is_reported_on_the_client_step()
	{
		_oauth.DeviceCodeFailure = new YouTubeOAuthRejectedException("invalid_client", "invalid_client");

		var result = await SubmitClient();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("client"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("TVs and Limited Input devices"));
		});
	}

	[Test]
	public async Task A_client_rejected_while_polling_sends_the_user_back_to_the_client_step()
	{
		await SubmitClient();
		_oauth.PollResults.Enqueue(new YouTubeTokenPoll(YouTubeTokenPollStatus.Rejected, ErrorCode: "invalid_client"));

		var result = await Continue();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("client"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("client ID and client secret"));
		});
	}

	[Test]
	public async Task Polling_sends_the_secret_and_reshows_the_code_while_nobody_approved()
	{
		await SubmitClient();

		var result = await Continue();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("link"));
			Assert.That(_oauth.PolledWith, Is.All.EqualTo((ClientId, ClientSecret)));
			Assert.That(_oauth.Calls.Count(call => call.StartsWith("device", StringComparison.Ordinal)), Is.EqualTo(1));
			Assert.That(_delays.Aggregate(TimeSpan.Zero, (total, delay) => total + delay),
				Is.LessThanOrEqualTo(TimeSpan.FromSeconds(90)));
		});
	}

	[Test]
	public async Task Slow_down_widens_the_interval_by_five_seconds()
	{
		await SubmitClient();
		_oauth.PollResults.Enqueue(new YouTubeTokenPoll(YouTubeTokenPollStatus.SlowDown));

		await Continue();

		Assert.That(_delays[0], Is.EqualTo(TimeSpan.FromSeconds(10)));
	}

	[Test]
	public async Task An_expired_code_is_replaced_by_a_new_one()
	{
		await SubmitClient();
		_oauth.PollResults.Enqueue(new YouTubeTokenPoll(YouTubeTokenPollStatus.Expired));

		var result = await Continue();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(TestLocalization.Resolve(result.NextStep!.Description), Does.Contain("expired"));
			Assert.That(_oauth.Calls.Count(call => call.StartsWith("device", StringComparison.Ordinal)), Is.EqualTo(2));
		});
	}

	[Test]
	public async Task A_declined_request_is_an_error_with_a_fresh_code()
	{
		await SubmitClient();
		_oauth.PollResults.Enqueue(new YouTubeTokenPoll(YouTubeTokenPollStatus.Denied));

		var result = await Continue();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("link"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("declined"));
		});
	}

	[Test]
	public async Task Approval_completes_with_the_channel_and_keeps_every_credential_secret()
	{
		await SubmitClient(25_000);
		var tokens = new YouTubeTokens("access-token",
			"refresh-token",
			YouTubeTestSupport.Now.AddHours(1),
			[YouTubeOAuthEndpoints.Scope]);
		_oauth.PollResults.Enqueue(new YouTubeTokenPoll(YouTubeTokenPollStatus.Success, tokens));

		var result = await Continue();

		var values = result.Values!;
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("YouTube (My Channel)"));
			Assert.That(_apiRequests, Is.EqualTo(new[] { (ClientId, 25_000, "access-token") }));
			Assert.That(values[YouTubeConfigKeys.ClientId], Is.EqualTo(ConfigFlowValue.Plain(ClientId)));
			Assert.That(values[YouTubeConfigKeys.ClientSecret], Is.EqualTo(ConfigFlowValue.Secret(ClientSecret)));
			Assert.That(values[YouTubeConfigKeys.AccessToken], Is.EqualTo(ConfigFlowValue.Secret("access-token")));
			Assert.That(values[YouTubeConfigKeys.RefreshToken], Is.EqualTo(ConfigFlowValue.Secret("refresh-token")));
			Assert.That(values[YouTubeConfigKeys.DailyQuota], Is.EqualTo(ConfigFlowValue.Plain("25000")));
			Assert.That(values[YouTubeConfigKeys.ChannelId], Is.EqualTo(ConfigFlowValue.Plain("UCabc")));
			Assert.That(values[YouTubeConfigKeys.ChannelTitle], Is.EqualTo(ConfigFlowValue.Plain("My Channel")));
			Assert.That(values[YouTubeConfigKeys.ChannelHandle], Is.EqualTo(ConfigFlowValue.Plain("@mychannel")));
			Assert.That(values[YouTubeConfigKeys.Scopes], Is.EqualTo(ConfigFlowValue.Plain(YouTubeOAuthEndpoints.Scope)));
			Assert.That(values.Keys, Is.SupersetOf(new[]
			{
				YouTubeConfigKeys.ConnectedAt, YouTubeConfigKeys.ExpiresAt
			}));
			Assert.That(values.Where(pair => pair.Value.IsSecret).Select(pair => pair.Key),
				Is.EquivalentTo(new[]
				{
					YouTubeConfigKeys.ClientSecret, YouTubeConfigKeys.AccessToken, YouTubeConfigKeys.RefreshToken
				}));
		});
	}

	[Test]
	public async Task A_google_account_without_a_channel_cannot_complete()
	{
		_api.Channel = null;
		await SubmitClient();
		_oauth.PollResults.Enqueue(new YouTubeTokenPoll(YouTubeTokenPollStatus.Success,
			new YouTubeTokens("a", "r", YouTubeTestSupport.Now.AddHours(1), [])));

		var result = await Continue();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("no YouTube channel"));
		});
	}

	[Test]
	public async Task A_failed_channel_lookup_retries_with_the_issued_tokens_instead_of_polling_again()
	{
		_api.Failures["channel"] = new YouTubeTransientException("offline");
		await SubmitClient();
		_oauth.PollResults.Enqueue(new YouTubeTokenPoll(YouTubeTokenPollStatus.Success,
			new YouTubeTokens("a", "r", YouTubeTestSupport.Now.AddHours(1), [])));

		var failed = await Continue();
		_api.Failures.Clear();
		var polls = _oauth.Calls.Count(call => call.StartsWith("poll", StringComparison.Ordinal));
		var completed = await Continue();

		Assert.Multiple(() =>
		{
			Assert.That(failed.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(completed.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(_oauth.Calls.Count(call => call.StartsWith("poll", StringComparison.Ordinal)),
				Is.EqualTo(polls));
		});
	}

	private Task<ConfigFlowResult> SubmitClient(object? quota = null)
		=> _flow.SubmitAsync("client",
			new Dictionary<string, object?>
			{
				[YouTubeConfigKeys.ClientId] = ClientId,
				[YouTubeConfigKeys.ClientSecret] = ClientSecret,
				[YouTubeConfigKeys.DailyQuota] = quota
			},
			null!,
			CancellationToken.None);

	private Task<ConfigFlowResult> Continue()
		=> _flow.SubmitAsync("link", new Dictionary<string, object?>(), null!, CancellationToken.None);
}
