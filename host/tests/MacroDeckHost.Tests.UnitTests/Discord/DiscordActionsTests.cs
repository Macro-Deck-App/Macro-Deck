using MacroDeckHost.Integrations.Discord;
using MacroDeckHost.Integrations.Discord.Actions;
using MacroDeckHost.Integrations.Discord.Rpc;
using MacroDeckHost.Integrations.Discord.Webhooks;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk.Actions;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordActionsTests
{
	private static readonly IReadOnlyList<IActionDefinition> _actions =
		DiscordActions.Create(() => null, new StubWebhookClient());

	private static readonly string[] _switchableSettings =
		["mute", "deafen", "noise-suppression", "echo-cancellation", "automatic-gain-control"];

	[Test]
	public void Every_action_id_is_unique()
	{
		Assert.That(_actions.Select(a => a.Id), Is.Unique);
	}

	[Test]
	public void Every_action_has_a_name_and_a_description()
	{
		Assert.Multiple(() =>
		{
			foreach (var action in _actions)
			{
				Assert.That(TestLocalization.Resolve(action.Name), Is.Not.Empty, action.Id);
				Assert.That(TestLocalization.Resolve(action.Description), Is.Not.Empty, action.Id);
			}
		});
	}

	[Test]
	public void The_switchable_settings_default_to_toggling()
	{
		var switchable = _actions
			.Where(a => a.Parameters.Any(p => p.Name == DiscordActionParameters.StateParameter))
			.ToList();

		Assert.That(switchable.Select(a => a.Id), Is.EquivalentTo(_switchableSettings));

		Assert.Multiple(() =>
		{
			foreach (var action in switchable)
			{
				var selector = action.Parameters.Single(p => p.Name == DiscordActionParameters.StateParameter);
				Assert.That(selector.DefaultValue, Is.EqualTo(DiscordActionParameters.StateToggle), action.Id);
				Assert.That(selector.Options, Has.Count.EqualTo(3), action.Id);
			}
		});
	}

	[Test]
	public void The_webhook_action_needs_no_discord_connection()
	{
		var webhook = _actions.Single(a => a.Id == "execute-webhook");

		Assert.That(webhook.CreateExecutor(), Is.Not.Null);
	}

	[Test]
	public void The_volume_actions_use_the_ranges_discord_accepts()
	{
		var input = _actions.Single(a => a.Id == "set-input-volume");
		var output = _actions.Single(a => a.Id == "set-output-volume");

		Assert.Multiple(() =>
		{
			Assert.That(input.Parameters.Single(p => p.Name == "volume").Max, Is.EqualTo(100));
			Assert.That(output.Parameters.Single(p => p.Name == "volume").Max, Is.EqualTo(200));
		});
	}

	[Test]
	public void The_join_action_offers_live_server_and_channel_pickers()
	{
		var join = _actions.Single(a => a.Id == "join-voice-channel");

		Assert.That(join, Is.InstanceOf<IDynamicOptionsActionDefinition>());
		Assert.Multiple(() =>
		{
			Assert.That(join.Parameters.Single(p => p.Name == "guildId").Type,
				Is.EqualTo(ActionParameterType.DynamicChoice));
			Assert.That(join.Parameters.Single(p => p.Name == "channelId").Type,
				Is.EqualTo(ActionParameterType.DynamicChoice));
			Assert.That(join.Parameters.Single(p => p.Name == "force").DefaultValue, Is.EqualTo(true));
		});
	}

	[Test]
	public async Task The_channel_picker_is_empty_until_a_server_is_chosen()
	{
		var join = (IDynamicOptionsActionDefinition)_actions.Single(a => a.Id == "join-voice-channel");

		var result = await join.GetDynamicOptionsAsync(new DynamicOptionsContext
			{
				ParameterName = "channelId",
				CurrentParameters = new Dictionary<string, object?>(StringComparer.Ordinal)
			},
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Options, Is.Empty);
			Assert.That(result.AllowsCustomValue, Is.True, "an id the user knows must stay enterable");
		});
	}

	[Test]
	public async Task Actions_do_nothing_when_discord_is_not_set_up()
	{
		foreach (var action in _actions.Where(a => a.Id != "execute-webhook"))
		{
			var result = await action.CreateExecutor().ExecuteAsync(Context());

			Assert.Multiple(() =>
			{
				Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed), action.Id);
				Assert.That(result.ErrorCode, Is.EqualTo(ActionErrorCodes.NotConfigured), action.Id);
			});
		}
	}

	[Test]
	public async Task Pressing_mute_once_while_deafened_puts_the_microphone_back_live()
	{
		var client = BuildClient(deaf: true);
		using var connection = await ConnectAsync(client);
		var mute = DiscordActions.Create(() => connection, new StubWebhookClient()).Single(a => a.Id == "mute");

		await mute.CreateExecutor().ExecuteAsync(Context());

		var call = client.CallsTo("SET_VOICE_SETTINGS").Single();
		Assert.That(call.ArgsJson, Is.EqualTo("""{"mute":false,"deaf":false}"""));
	}

	[Test]
	public async Task Pressing_mute_while_live_only_mutes()
	{
		var client = BuildClient();
		using var connection = await ConnectAsync(client);
		var mute = DiscordActions.Create(() => connection, new StubWebhookClient()).Single(a => a.Id == "mute");

		await mute.CreateExecutor().ExecuteAsync(Context());

		var call = client.CallsTo("SET_VOICE_SETTINGS").Single();
		Assert.That(call.ArgsJson, Is.EqualTo("""{"mute":true}"""));
	}

	[Test]
	public async Task A_moderator_mute_does_not_freeze_the_mute_action()
	{
		var client = BuildClient(serverMuted: true);
		using var connection = await ConnectAsync(client);
		var mute = DiscordActions.Create(() => connection, new StubWebhookClient()).Single(a => a.Id == "mute");
		Assert.That(connection.State.ServerMuted, Is.True, "the moderator flag has to reach the state first");

		await mute.CreateExecutor().ExecuteAsync(Context());

		var call = client.CallsTo("SET_VOICE_SETTINGS").Single();
		Assert.That(call.ArgsJson, Is.EqualTo("""{"mute":true}"""));
	}

	[Test]
	public async Task The_deafen_action_still_sends_deaf_with_no_mute_field()
	{
		var client = BuildClient();
		using var connection = await ConnectAsync(client);
		var deafen = DiscordActions.Create(() => connection, new StubWebhookClient()).Single(a => a.Id == "deafen");

		await deafen.CreateExecutor().ExecuteAsync(Context());

		var call = client.CallsTo("SET_VOICE_SETTINGS").Single();
		Assert.That(call.ArgsJson, Is.EqualTo("""{"deaf":true}"""));
	}

	[Test]
	public async Task The_noise_suppression_action_sends_only_the_noise_suppression_field()
	{
		var client = BuildClient();
		using var connection = await ConnectAsync(client);
		var action = DiscordActions.Create(() => connection, new StubWebhookClient())
			.Single(a => a.Id == "noise-suppression");

		await action.CreateExecutor().ExecuteAsync(Context());

		var call = client.CallsTo("SET_VOICE_SETTINGS").Single();
		Assert.That(call.ArgsJson, Is.EqualTo("""{"noise_suppression":true}"""));
	}

	[Test]
	public async Task The_echo_cancellation_action_sends_only_the_echo_cancellation_field()
	{
		var client = BuildClient();
		using var connection = await ConnectAsync(client);
		var action = DiscordActions.Create(() => connection, new StubWebhookClient())
			.Single(a => a.Id == "echo-cancellation");

		await action.CreateExecutor().ExecuteAsync(Context());

		var call = client.CallsTo("SET_VOICE_SETTINGS").Single();
		Assert.That(call.ArgsJson, Is.EqualTo("""{"echo_cancellation":true}"""));
	}

	[Test]
	public async Task Noise_suppression_succeeds_when_discord_confirms_it()
	{
		var client = BuildClient();
		using var connection = await ConnectAsync(client);
		var action = DiscordActions.Create(() => connection, new StubWebhookClient())
			.Single(a => a.Id == "noise-suppression");

		Assert.DoesNotThrowAsync(() => action.CreateExecutor().ExecuteAsync(Context()));
	}

	[Test]
	public async Task Noise_suppression_fails_when_discord_returns_the_old_value()
	{
		var client = BuildClient(echoVoiceSettings: false)
			.Responds("SET_VOICE_SETTINGS", """{"noise_suppression":false}""");
		using var connection = await ConnectAsync(client);
		var action = DiscordActions.Create(() => connection, new StubWebhookClient())
			.Single(a => a.Id == "noise-suppression");

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(TestLocalization.Resolve(result.ErrorMessage),
			Does.Contain("noise_suppression").And.Contain("unchanged"));
	}

	[Test]
	public async Task Echo_cancellation_fails_when_discord_omits_the_field()
	{
		var client = BuildClient(echoVoiceSettings: false).Responds("SET_VOICE_SETTINGS", "{}");
		using var connection = await ConnectAsync(client);
		var action = DiscordActions.Create(() => connection, new StubWebhookClient())
			.Single(a => a.Id == "echo-cancellation");

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(TestLocalization.Resolve(result.ErrorMessage),
			Does.Contain("echo_cancellation").And.Contain("did not report"));
	}

	[Test]
	public async Task A_rejected_command_fails_the_action()
	{
		var client = BuildClient(echoVoiceSettings: false)
			.Fails("SET_VOICE_SETTINGS", new DiscordRpcException(4000, "Invalid payload"));
		using var connection = await ConnectAsync(client);
		var action = DiscordActions.Create(() => connection, new StubWebhookClient())
			.Single(a => a.Id == "noise-suppression");

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("rejected").And.Contain("4000"));
	}

	[Test]
	public async Task The_mute_action_fails_when_discord_ignores_it()
	{
		var client = BuildClient(echoVoiceSettings: false).Responds("SET_VOICE_SETTINGS", "{}");
		using var connection = await ConnectAsync(client);
		var mute = DiscordActions.Create(() => connection, new StubWebhookClient()).Single(a => a.Id == "mute");

		var result = await mute.CreateExecutor().ExecuteAsync(Context());

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
	}

	[Test]
	public async Task The_volume_action_fails_when_discord_reports_a_different_volume()
	{
		var client = BuildClient(echoVoiceSettings: false)
			.Responds("SET_VOICE_SETTINGS", """{"input":{"volume":10}}""");
		using var connection = await ConnectAsync(client);
		var action = DiscordActions.Create(() => connection, new StubWebhookClient())
			.Single(a => a.Id == "set-input-volume");

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object>(StringComparer.Ordinal)
			{
				["mode"] = "set",
				["volume"] = 80d
			}));

		Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("input.volume"));
	}

	[Test]
	public async Task The_voice_mode_action_fails_when_discord_keeps_the_old_mode()
	{
		var client = BuildClient(echoVoiceSettings: false)
			.Responds("SET_VOICE_SETTINGS", """{"mode":{"type":"VOICE_ACTIVITY"}}""");
		using var connection = await ConnectAsync(client);
		var action = DiscordActions.Create(() => connection, new StubWebhookClient())
			.Single(a => a.Id == "set-voice-mode");

		var result = await action.CreateExecutor()
			.ExecuteAsync(Context(new Dictionary<string, object>(StringComparer.Ordinal)
			{
				["voiceMode"] = "PUSH_TO_TALK"
			}));

		Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("mode.type"));
	}

	[Test]
	public async Task A_configured_but_closed_discord_fails_the_action()
	{
		var client = BuildClient();
		using var connection = new DiscordConnection(() => client,
			new FakeDiscordOAuthClient(),
			"123456",
			"secret",
			new DiscordTokens("stored-access", "stored-refresh", ExpiresAt: null, "rpc rpc.voice.read rpc.voice.write"),
			(_, _) => Task.CompletedTask,
			events: null,
			TimeSpan.FromMinutes(5));
		var action = DiscordActions.Create(() => connection, new StubWebhookClient()).Single(a => a.Id == "mute");

		var result = await action.CreateExecutor().ExecuteAsync(Context());

		Assert.That(result.Status, Is.EqualTo(ActionResultStatus.Failed));
		Assert.That(TestLocalization.Resolve(result.ErrorMessage), Does.Contain("not running"));
	}

	[Test]
	public async Task The_mute_action_reports_whether_the_user_is_muted()
	{
		using var connection = await ConnectAsync(BuildClient(deaf: true));
		var provider = (IStateProviderActionDefinition)DiscordActions
			.Create(() => connection, new StubWebhookClient())
			.Single(a => a.Id == "mute");

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("muted"));
	}

	[Test]
	public async Task The_voice_toggles_are_unavailable_while_discord_is_not_running()
	{
		var provider = (IStateProviderActionDefinition)DiscordActions
			.Create(() => null, new StubWebhookClient())
			.Single(a => a.Id == "deafen");

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(snapshot!.ActiveStateId, Is.EqualTo("unavailable"));
			Assert.That(snapshot.States, Is.Not.Empty);
		});
	}

	[Test]
	public async Task The_voice_mode_action_reports_the_mode_discord_is_in()
	{
		var client = BuildClient();
		client.Responds("GET_VOICE_SETTINGS", """{"mute":false,"deaf":false,"mode":{"type":"PUSH_TO_TALK"}}""");
		using var connection = await ConnectAsync(client);
		var provider = (IStateProviderActionDefinition)DiscordActions
			.Create(() => connection, new StubWebhookClient())
			.Single(a => a.Id == "set-voice-mode");

		var snapshot = await provider.GetActionStateAsync(_noParameters, CancellationToken.None);

		Assert.That(snapshot!.ActiveStateId, Is.EqualTo("push-to-talk"));
	}

	private static readonly Dictionary<string, object?> _noParameters = new(StringComparer.Ordinal);

	private static ActionExecutionContext Context() => Context(new Dictionary<string, object>(StringComparer.Ordinal));

	private static ActionExecutionContext Context(Dictionary<string, object> parameters)
		=> new() { Parameters = parameters };

	private static FakeDiscordRpcClient BuildClient(
		bool deaf = false,
		bool serverMuted = false,
		bool echoVoiceSettings = true)
	{
		var client = new FakeDiscordRpcClient()
			.Responds("AUTHENTICATE",
				"""{"user":{"id":"me","username":"tester"},"scopes":["rpc","rpc.voice.read","rpc.voice.write"]}""")
			.Responds("GET_VOICE_SETTINGS", """{"mute":false,"deaf":""" + (deaf ? "true" : "false") + "}")
			.Responds("GET_SELECTED_VOICE_CHANNEL",
				serverMuted
					? """
					  {"id":"555","name":"General","guild_id":"999",
					  "voice_states":[{"user":{"id":"me"},"voice_state":{"mute":true,"deaf":false}}]}
					  """
					: "null")
			.Responds("GET_GUILDS", """{"guilds":[{"id":"999","name":"My Server"}]}""");

		if (echoVoiceSettings)
		{
			client.RespondsWithEcho("SET_VOICE_SETTINGS");
		}

		return client;
	}

	private static async Task<DiscordConnection> ConnectAsync(FakeDiscordRpcClient client)
	{
		var connection = new DiscordConnection(() => client,
			new FakeDiscordOAuthClient(),
			"123456",
			"secret",
			new DiscordTokens("stored-access", "stored-refresh", ExpiresAt: null, "rpc rpc.voice.read rpc.voice.write"),
			(_, _) => Task.CompletedTask,
			events: null,
			TimeSpan.FromMinutes(5));

		connection.Start();
		for (var attempt = 0; attempt < 300 && !connection.State.IsConnected; attempt++)
		{
			await Task.Delay(10);
		}

		Assert.That(connection.State.IsConnected, Is.True, "the fake connection did not come up in time");
		return connection;
	}

	private sealed class StubWebhookClient : IDiscordWebhookClient
	{
		public Task<string?> ExecuteAsync(
			string webhookUrl,
			DiscordWebhookRequest request,
			CancellationToken cancellationToken)
			=> Task.FromResult<string?>(null);
	}
}
