using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.ConfigFlow;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchConfigFlowTests
{
	private FakeTwitchOAuthClient _client = null!;
	private TwitchConfigFlow _flow = null!;
	private List<TimeSpan> _delays = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeTwitchOAuthClient();
		_delays = [];
		_flow = new TwitchConfigFlow(() => _client,
			(delay, _) =>
			{
				_delays.Add(delay);
				return Task.CompletedTask;
			});
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
	}

	[Test]
	public async Task Setup_starts_at_the_code_using_macro_decks_own_application()
	{
		var result = await _flow.StartAsync(null!, CancellationToken.None);

		var step = result.NextStep!;
		var codeValue = step.Instructions.SelectMany(i => i.Values).Single();
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(step.StepId, Is.EqualTo("link"));
			Assert.That(TestLocalization.Resolve(codeValue.Label), Is.EqualTo("Device code"));
			Assert.That(codeValue.Value, Is.EqualTo("ABCD-1234"));
			Assert.That(step.Links[0].Url, Is.EqualTo(_client.DeviceCode.VerificationUri));
			Assert.That(_client.UsedClientIds, Is.EqualTo(new[] { TwitchOAuthEndpoints.MacroDeckClientId }));
			Assert.That(_client.RequestedScopes[0], Is.EqualTo(TwitchScopes.Requested));
		});
	}

	[Test]
	public async Task The_own_client_id_is_an_advanced_field_and_never_required()
	{
		var step = (await _flow.StartAsync(null!, CancellationToken.None)).NextStep!;

		var field = step.AdvancedFields.Single();
		Assert.Multiple(() =>
		{
			Assert.That(step.Fields, Is.Empty, "the ordinary path asks for nothing");
			Assert.That(field.Name, Is.EqualTo(TwitchConfigKeys.ClientId));
			Assert.That(field.Required, Is.False);
			Assert.That(TestLocalization.Resolve(field.Description), Does.Contain("Leave empty"));
			Assert.That(TestLocalization.Resolve(field.Description), Does.Contain("Public"));
		});
	}

	[Test]
	public async Task Every_step_keeps_the_advanced_field_reachable()
	{
		var started = (await _flow.StartAsync(null!, CancellationToken.None)).NextStep!;
		var waiting = (await Submit()).NextStep!;
		_client.DeviceCodeFailure = new TwitchOAuthTransientException("offline");
		var retry = (await Submit("other-id")).NextStep!;

		Assert.Multiple(() =>
		{
			foreach (var step in new[] { started, waiting, retry })
			{
				Assert.That(step.AdvancedFields.Select(f => f.Name),
					Is.EqualTo(new[] { TwitchConfigKeys.ClientId }),
					TestLocalization.Resolve(step.Title));
			}
		});
	}

	[Test]
	public async Task A_pending_authorization_reshows_the_same_step_rather_than_erroring()
	{
		await _flow.StartAsync(null!, CancellationToken.None);

		var result = await Submit();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("link"), "continuing has to resume the same poll");
			Assert.That(_client.Calls.Count(call => call == "device"), Is.EqualTo(1), "the code must not change");
		});
	}

	[Test]
	public async Task Polling_stays_within_its_budget()
	{
		await _flow.StartAsync(null!, CancellationToken.None);

		await Submit();

		Assert.That(_delays.Aggregate(TimeSpan.Zero, (total, delay) => total + delay),
			Is.LessThanOrEqualTo(TimeSpan.FromSeconds(90)));
	}

	[Test]
	public async Task Slow_down_widens_the_interval()
	{
		await _flow.StartAsync(null!, CancellationToken.None);
		_client.PollResults.Enqueue(new TwitchTokenPollResult(TwitchTokenPollStatus.SlowDown));

		await Submit();

		Assert.That(_delays[0], Is.EqualTo(_client.DeviceCode.Interval + TimeSpan.FromSeconds(5)));
	}

	[Test]
	public async Task An_expired_device_code_is_replaced_by_a_fresh_one()
	{
		await _flow.StartAsync(null!, CancellationToken.None);
		_client.PollResults.Enqueue(new TwitchTokenPollResult(TwitchTokenPollStatus.Expired));

		var result = await Submit();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(TestLocalization.Resolve(result.NextStep!.Description), Does.Contain("expired"));
			Assert.That(_client.Calls.Count(call => call == "device"), Is.EqualTo(2));
		});
	}

	[Test]
	public async Task A_declined_authorization_says_so()
	{
		await _flow.StartAsync(null!, CancellationToken.None);
		_client.PollResults.Enqueue(new TwitchTokenPollResult(TwitchTokenPollStatus.Denied));

		var result = await Submit();

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("declined"));
		});
	}

	[Test]
	public async Task An_own_client_id_replaces_the_code_and_is_used_from_then_on()
	{
		await _flow.StartAsync(null!, CancellationToken.None);

		var result = await Submit("my-own-client-id");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Step));
			Assert.That(_client.UsedClientIds,
				Is.EqualTo(new[] { TwitchOAuthEndpoints.MacroDeckClientId, "my-own-client-id" }));
			Assert.That(result.NextStep!.AdvancedFields.Single().DefaultValue,
				Is.EqualTo("my-own-client-id"),
				"the client resets a step's values to its defaults, so the entry has to be carried");
		});
	}

	[Test]
	public async Task Clearing_the_own_client_id_goes_back_to_macro_decks_application()
	{
		await _flow.StartAsync(null!, CancellationToken.None);
		await Submit("my-own-client-id");

		await Submit(string.Empty);

		Assert.That(_client.UsedClientIds,
			Is.EqualTo(new[]
			{
				TwitchOAuthEndpoints.MacroDeckClientId,
				"my-own-client-id",
				TwitchOAuthEndpoints.MacroDeckClientId
			}));
	}

	[Test]
	public async Task Keeping_the_same_own_client_id_keeps_polling_rather_than_restarting()
	{
		await _flow.StartAsync(null!, CancellationToken.None);
		await Submit("my-own-client-id");

		await Submit("my-own-client-id");

		Assert.That(_client.Calls.Count(call => call == "device"), Is.EqualTo(2));
	}

	[Test]
	public async Task A_rejected_own_client_id_points_at_the_client_type_and_the_way_back()
	{
		await _flow.StartAsync(null!, CancellationToken.None);
		_client.DeviceCodeFailure = new TwitchOAuthRejectedException("invalid client");

		var result = await Submit("my-own-client-id");

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("Public"));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("Clear the field"));
		});
	}

	[Test]
	public async Task A_rejected_shipped_application_offers_the_own_client_id_instead()
	{
		_client.DeviceCodeFailure = new TwitchOAuthRejectedException("invalid client");

		var result = await _flow.StartAsync(null!, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("application of your own"));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("link"), "continuing has to be able to retry");
		});
	}

	[Test]
	public async Task An_unreachable_twitch_is_retryable()
	{
		_client.DeviceCodeFailure = new TwitchOAuthTransientException("offline");

		var result = await _flow.StartAsync(null!, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("could not be reached"));
		});
	}

	[Test]
	public async Task An_approved_authorization_completes_with_the_tokens_stored_as_secrets()
	{
		await _flow.StartAsync(null!, CancellationToken.None);
		_client.PollResults.Enqueue(new TwitchTokenPollResult(TwitchTokenPollStatus.Success,
			FakeTwitchOAuthClient.Tokens("access", "refresh", TimeSpan.FromHours(4))));

		var result = await Submit();

		var values = result.Values!;
		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Complete));
			Assert.That(result.EntryTitle, Is.EqualTo("Twitch (streamer)"));
			Assert.That(values[TwitchConfigKeys.AccessToken].IsSecret, Is.True);
			Assert.That(values[TwitchConfigKeys.RefreshToken].IsSecret, Is.True);
			Assert.That(values[TwitchConfigKeys.AccessToken].Value, Is.EqualTo("access"));
			Assert.That(values[TwitchConfigKeys.RefreshToken].Value, Is.EqualTo("refresh"));
		});
	}

	[Test]
	public async Task The_completed_entry_carries_the_identity_the_account_model_needs()
	{
		await _flow.StartAsync(null!, CancellationToken.None);
		_client.PollResults.Enqueue(new TwitchTokenPollResult(TwitchTokenPollStatus.Success,
			FakeTwitchOAuthClient.Tokens("access", "refresh", TimeSpan.FromHours(4))));

		var values = (await Submit()).Values!;

		Assert.Multiple(() =>
		{
			Assert.That(values[TwitchConfigKeys.UserId].Value, Is.EqualTo("12345"));
			Assert.That(values[TwitchConfigKeys.UserId].IsSecret, Is.False);
			Assert.That(values[TwitchConfigKeys.Login].Value, Is.EqualTo("streamer"));
			Assert.That(values[TwitchConfigKeys.ConnectedAt].Value, Is.Not.Null.And.Not.Empty);
			Assert.That(values[TwitchConfigKeys.Scopes].Value, Is.EqualTo(TwitchScopes.Requested));
		});
	}

	[Test]
	public async Task The_completed_entry_records_which_application_authorized_it()
	{
		await _flow.StartAsync(null!, CancellationToken.None);
		await Submit("my-own-client-id");
		_client.PollResults.Enqueue(new TwitchTokenPollResult(TwitchTokenPollStatus.Success,
			FakeTwitchOAuthClient.Tokens("access", "refresh", TimeSpan.FromHours(4))));

		var values = (await Submit("my-own-client-id")).Values!;

		Assert.That(values[TwitchConfigKeys.ClientId].Value, Is.EqualTo("my-own-client-id"));
	}

	[Test]
	public async Task A_completed_default_setup_records_macro_decks_application()
	{
		await _flow.StartAsync(null!, CancellationToken.None);
		_client.PollResults.Enqueue(new TwitchTokenPollResult(TwitchTokenPollStatus.Success,
			FakeTwitchOAuthClient.Tokens("access", "refresh", TimeSpan.FromHours(4))));

		var values = (await Submit()).Values!;

		Assert.That(values[TwitchConfigKeys.ClientId].Value, Is.EqualTo(TwitchOAuthEndpoints.MacroDeckClientId));
	}

	[Test]
	public async Task An_unknown_step_offers_a_retry()
	{
		var result = await _flow.SubmitAsync("nonsense",
			new Dictionary<string, object?>(StringComparer.Ordinal),
			null!,
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Kind, Is.EqualTo(ConfigFlowResultKind.Error));
			Assert.That(result.NextStep!.StepId, Is.EqualTo("link"));
		});
	}

	private Task<ConfigFlowResult> Submit(string? clientId = null)
	{
		var input = new Dictionary<string, object?>(StringComparer.Ordinal);
		if (clientId is not null)
		{
			input[TwitchConfigKeys.ClientId] = clientId;
		}

		return _flow.SubmitAsync("link", input, null!, CancellationToken.None);
	}
}
