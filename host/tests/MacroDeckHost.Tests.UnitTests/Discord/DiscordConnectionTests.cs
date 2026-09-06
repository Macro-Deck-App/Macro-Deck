using MacroDeckHost.Integrations.Discord;
using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordConnectionTests
{
	private const string InGeneral =
		"""{"id":"555","name":"General","guild_id":"999","voice_states":[]}""";

	private static readonly string[] _channelScopedEvents =
		["VOICE_STATE_CREATE", "VOICE_STATE_UPDATE", "SPEAKING_START", "SPEAKING_STOP"];

	private static readonly string[] _globalEvents =
		["VOICE_SETTINGS_UPDATE", "VOICE_CHANNEL_SELECT", "VOICE_CONNECTION_STATUS"];

	private static readonly string[] _general = ["555"];
	private static readonly string[] _generalThenGaming = ["555", "777"];
	private static readonly string[] _myServer = ["My Server"];
	private static readonly string[] _voiceAndStage = ["Voice", "Stage"];
	private static readonly int[] _voiceChannelTypes = [2, 13];
	private static readonly string[] _noiseSuppressionOnly = ["noise_suppression"];

	private FakeDiscordRpcClient _client = null!;
	private FakeDiscordOAuthClient _oauth = null!;
	private List<DiscordTokens> _persisted = null!;

	[SetUp]
	public void SetUp()
	{
		_client = new FakeDiscordRpcClient()
			.Responds("AUTHENTICATE",
				"""{"user":{"id":"me","username":"tester"},"scopes":["rpc","rpc.voice.read","rpc.voice.write"]}""")
			.Responds("GET_VOICE_SETTINGS", """{"mute":false,"deaf":false,"input":{"volume":80}}""")
			.Responds("GET_SELECTED_VOICE_CHANNEL", InGeneral)
			.Responds("GET_GUILDS", """{"guilds":[{"id":"999","name":"My Server"}]}""");
		_oauth = new FakeDiscordOAuthClient();
		_persisted = [];
	}

	[TearDown]
	public void TearDown() => _client.Dispose();

	[Test]
	public async Task Connecting_authenticates_subscribes_and_seeds_the_state()
	{
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.State.IsConnected);

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.UserName, Is.EqualTo("tester"));
			Assert.That(connection.State.InputVolume, Is.EqualTo(80));
			Assert.That(connection.State.VoiceChannelName, Is.EqualTo("General"));
			Assert.That(connection.State.VoiceGuildName, Is.EqualTo("My Server"));
		});

		var authenticate = _client.CallsTo("AUTHENTICATE").Single();
		Assert.That(authenticate.ArgsJson, Does.Contain("\"access_token\":\"stored-access\""));

		var globalSubscriptions = _client.CallsTo("SUBSCRIBE")
			.Where(c => c.ArgsJson == "null")
			.Select(c => c.EventName);
		Assert.That(globalSubscriptions, Is.EquivalentTo(_globalEvents));
	}

	[Test]
	public async Task Connecting_subscribes_the_channel_scoped_events_for_the_current_channel()
	{
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.State.IsConnected);

		var subscribed = _client.CallsTo("SUBSCRIBE")
			.Where(c => c.ArgsJson.Contains("\"channel_id\":\"555\"", StringComparison.Ordinal))
			.Select(c => c.EventName);
		Assert.That(subscribed, Is.EquivalentTo(_channelScopedEvents));
	}

	[Test]
	public async Task Switching_channels_moves_the_subscriptions_to_the_new_channel()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		_client.Responds("GET_SELECTED_VOICE_CHANNEL",
			"""{"id":"777","name":"Gaming","guild_id":"999","voice_states":[]}""");
		_client.RaiseEvent("VOICE_CHANNEL_SELECT", """{"channel_id":"777","guild_id":"999"}""");

		await WaitForAsync(() => connection.State.VoiceChannelId == "777");

		Assert.Multiple(() =>
		{
			Assert.That(_client.UnsubscribedChannelIds, Is.EqualTo(_general));
			Assert.That(_client.SubscribedChannelIds, Is.EqualTo(_generalThenGaming));
			Assert.That(connection.State.VoiceChannelName, Is.EqualTo("Gaming"));
		});

		var unsubscribed = _client.CallsTo("UNSUBSCRIBE").Select(c => c.EventName);
		Assert.That(unsubscribed, Is.EquivalentTo(_channelScopedEvents));
	}

	[Test]
	public async Task A_server_mute_in_the_new_channel_reaches_the_state()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		_client.RaiseEvent("VOICE_STATE_UPDATE",
			"""{"user":{"id":"me"},"voice_state":{"mute":true,"deaf":false}}""");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.ServerMuted, Is.True);
			Assert.That(connection.State.EffectivelyMuted, Is.True);
			Assert.That(connection.State.SelfMuted, Is.False, "a server mute is not a self mute");
		});
	}

	[Test]
	public async Task Leaving_a_channel_unsubscribes_and_forgets_the_channel()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		_client.RaiseEvent("VOICE_CHANNEL_SELECT", """{"channel_id":null,"guild_id":null}""");

		await WaitForAsync(() => !connection.State.InVoiceChannel);

		Assert.Multiple(() =>
		{
			Assert.That(_client.UnsubscribedChannelIds, Is.EqualTo(_general));
			Assert.That(connection.State.VoiceChannelName, Is.Null);
			Assert.That(connection.State.ServerMuted, Is.False);
		});
	}

	[Test]
	public async Task A_voice_settings_push_updates_the_state()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		_client.RaiseEvent("VOICE_SETTINGS_UPDATE", """{"mute":true,"mode":{"type":"PUSH_TO_TALK"}}""");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.SelfMuted, Is.True);
			Assert.That(connection.State.VoiceMode, Is.EqualTo("PUSH_TO_TALK"));
			Assert.That(connection.State.InputVolume, Is.EqualTo(80), "a partial push keeps the other values");
		});
	}

	[Test]
	public async Task A_deafen_push_with_mute_false_silences_the_microphone()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		_client.RaiseEvent("VOICE_SETTINGS_UPDATE", """{"mute":false,"deaf":true}""");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.SelfMuted, Is.False);
			Assert.That(connection.State.EffectivelyMuted, Is.True);
		});
	}

	[Test]
	public async Task Speaking_is_tracked_for_the_local_user_only()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		_client.RaiseEvent("SPEAKING_START", """{"user_id":"someone-else"}""");
		Assert.That(connection.State.SelfSpeaking, Is.False);

		_client.RaiseEvent("SPEAKING_START", """{"user_id":"me"}""");
		Assert.That(connection.State.SelfSpeaking, Is.True);

		_client.RaiseEvent("SPEAKING_STOP", """{"user_id":"me"}""");
		Assert.That(connection.State.SelfSpeaking, Is.False);
	}

	[Test]
	public async Task Notifications_are_not_subscribed_without_the_scope()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		Assert.That(_client.CallsTo("SUBSCRIBE").Select(c => c.EventName), Does.Not.Contain("NOTIFICATION_CREATE"));
	}

	[Test]
	public async Task Notifications_are_subscribed_when_the_scope_was_granted()
	{
		_client.Responds("AUTHENTICATE",
			"""{"user":{"id":"me"},"scopes":["rpc","rpc.voice.read","rpc.voice.write","rpc.notifications.read"]}""");
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		Assert.That(_client.CallsTo("SUBSCRIBE").Select(c => c.EventName), Does.Contain("NOTIFICATION_CREATE"));
	}

	[Test]
	public async Task An_expired_access_token_is_refreshed_before_authenticating()
	{
		using var connection = Create(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-1));
		connection.Start();

		await WaitForAsync(() => connection.State.IsConnected);

		Assert.Multiple(() =>
		{
			Assert.That(_oauth.RefreshCount, Is.EqualTo(1));
			Assert.That(_client.CallsTo("AUTHENTICATE").Single().ArgsJson,
				Does.Contain("\"access_token\":\"fresh-access\""));
			Assert.That(_persisted, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_rejected_token_is_refreshed_once_and_retried()
	{
		_client.Fails("AUTHENTICATE", new DiscordRpcException(4009, "Invalid token"));
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.State.IsConnected);

		Assert.Multiple(() =>
		{
			Assert.That(_oauth.RefreshCount, Is.EqualTo(1));
			Assert.That(_client.CallsTo("AUTHENTICATE").Count(), Is.EqualTo(2));
			Assert.That(connection.NeedsReauthorization, Is.False);
		});
	}

	[Test]
	public async Task An_authorization_that_cannot_be_repaired_stops_the_retry_loop()
	{
		_client.Fails("AUTHENTICATE", new DiscordRpcException(4009, "Invalid token"));
		_oauth.Exception = new DiscordOAuthException("Discord rejected the Client Secret.");
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.NeedsReauthorization);

		Assert.That(connection.IsConnected, Is.False);
	}

	[Test]
	public async Task Discord_not_running_schedules_a_retry_instead_of_reporting_an_authorization_problem()
	{
		_client.ConnectException = new DiscordIpcUnavailableException("no endpoint");
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => _client.ConnectCount > 0 && _client.IsDisposed);

		Assert.Multiple(() =>
		{
			Assert.That(connection.NeedsReauthorization, Is.False);
			Assert.That(connection.AccessDenied, Is.False);
			Assert.That(connection.State.IsConnected, Is.False);
		});
	}

	[Test]
	public async Task A_refused_endpoint_is_reported_as_a_privilege_problem()
	{
		_client.ConnectException = new DiscordIpcUnavailableException("refused", accessDenied: true);
		using var connection = Create();
		connection.Start();

		await WaitForAsync(() => connection.AccessDenied);

		Assert.That(connection.NeedsReauthorization, Is.False);
	}

	[Test]
	public async Task Losing_the_connection_clears_the_state()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);
		_client.RaiseEvent("VOICE_SETTINGS_UPDATE", """{"mute":true}""");

		_client.RaiseDisconnected("closed");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State, Is.EqualTo(DiscordState.Disconnected));
			Assert.That(connection.State.SelfMuted, Is.False);
			Assert.That(connection.State.VoiceChannelName, Is.Null);
		});
	}

	[Test]
	public async Task A_dropped_connection_is_retried()
	{
		using var connection = Create(reconnectDelay: TimeSpan.FromMilliseconds(20));
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		_client.RaiseDisconnected("dropped");

		await WaitForAsync(() => _client.ConnectCount >= 2);
		Assert.That(connection.NeedsReauthorization, Is.False);
	}

	[Test]
	public async Task Leaving_a_voice_channel_sends_an_explicit_null_channel_id()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		await connection.LeaveVoiceChannelAsync();

		var call = _client.CallsTo("SELECT_VOICE_CHANNEL").Single();
		Assert.That(call.ArgsJson, Is.EqualTo("""{"channel_id":null}"""));
	}

	[Test]
	public async Task Joining_a_voice_channel_passes_the_force_flag()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		await connection.JoinVoiceChannelAsync("777", force: true);

		var call = _client.CallsTo("SELECT_VOICE_CHANNEL").Single();
		Assert.That(call.ArgsJson, Is.EqualTo("""{"channel_id":"777","force":true}"""));
	}

	[Test]
	public async Task Clearing_the_rich_presence_sends_an_explicit_null_activity()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		await connection.SetActivityAsync(activity: null);

		var call = _client.CallsTo("SET_ACTIVITY").Single();
		Assert.That(call.ArgsJson, Does.Contain("\"activity\":null"));
	}

	[Test]
	public async Task A_voice_settings_command_applies_its_own_response()
	{
		_client.Responds("SET_VOICE_SETTINGS", """{"mute":true,"deaf":true}""");
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { Mute = true });

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.SelfMuted, Is.True);
			Assert.That(connection.State.SelfDeafened, Is.True);
		});
	}

	[Test]
	public async Task A_rejected_command_does_not_take_the_connection_down()
	{
		_client.Fails("SET_VOICE_SETTINGS", new DiscordRpcException(4000, "Invalid payload"));
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		var result = await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { Mute = true });

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.IsConnected, Is.True);
			Assert.That(result.Outcome, Is.EqualTo(DiscordVoiceSettingsOutcome.Rejected));
			Assert.That(result.Message, Does.Contain("4000"));
		});
	}

	[Test]
	public async Task A_code_less_failure_is_not_reported_as_a_discord_rejection()
	{
		_client.Fails("SET_VOICE_SETTINGS", new DiscordRpcException("The Discord connection was closed."));
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		var result = await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { Mute = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(DiscordVoiceSettingsOutcome.Failed));
			Assert.That(result.Message, Does.Not.Contain("code"));
		});
	}

	[Test]
	public async Task Commands_are_ignored_while_disconnected()
	{
		using var connection = Create();

		var result = await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { Mute = true });

		Assert.Multiple(() =>
		{
			Assert.That(_client.Calls, Is.Empty);
			Assert.That(result.Outcome, Is.EqualTo(DiscordVoiceSettingsOutcome.NotConnected));
		});
	}

	[Test]
	public async Task A_voice_settings_command_that_discord_confirms_is_reported_as_confirmed()
	{
		_client.Responds("SET_VOICE_SETTINGS", """{"mute":true}""");
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		var result = await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { Mute = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.IsConfirmed, Is.True);
			Assert.That(result.UnappliedFields, Is.Empty);
		});
	}

	[Test]
	public async Task Unapplied_fields_are_reported_with_their_reason()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		var result = await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { NoiseSuppression = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(DiscordVoiceSettingsOutcome.NotApplied));
			var field = result.UnappliedFields.Single();
			Assert.That(field.Field, Is.EqualTo("noise_suppression"));
			Assert.That(field.Reason, Is.EqualTo(DiscordFieldMismatch.Omitted));
		});
	}

	[Test]
	public async Task The_response_is_folded_into_state_even_when_it_disagrees_with_the_request()
	{
		_client.Responds("SET_VOICE_SETTINGS", """{"noise_suppression":false}""");
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		var result = await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { NoiseSuppression = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.IsConfirmed, Is.False);
			Assert.That(connection.State.NoiseSuppression, Is.False, "state must follow Discord, not the request");
		});
	}

	[Test]
	public async Task An_ignored_field_is_reported_until_a_later_confirmed_change_clears_it()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { NoiseSuppression = true });
		Assert.That(connection.IgnoredVoiceSettings, Is.EqualTo(_noiseSuppressionOnly));

		_client.Responds("SET_VOICE_SETTINGS", """{"noise_suppression":true}""");
		var result = await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { NoiseSuppression = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.IsConfirmed, Is.True);
			Assert.That(connection.HasIgnoredVoiceSettings, Is.False);
		});
	}

	[Test]
	public async Task A_confirmed_change_clears_only_the_field_it_named()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { NoiseSuppression = true });
		Assert.That(connection.IgnoredVoiceSettings, Is.EqualTo(_noiseSuppressionOnly));

		_client.Responds("SET_VOICE_SETTINGS", """{"echo_cancellation":true}""");
		await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { EchoCancellation = true });

		Assert.That(connection.IgnoredVoiceSettings, Is.EqualTo(_noiseSuppressionOnly));
	}

	[Test]
	public async Task Ignored_fields_are_forgotten_when_the_connection_drops()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { NoiseSuppression = true });
		Assert.That(connection.HasIgnoredVoiceSettings, Is.True);

		_client.RaiseDisconnected("closed");

		Assert.That(connection.HasIgnoredVoiceSettings, Is.False);
	}

	[Test]
	public async Task A_later_conflicting_push_wins_in_state_and_does_not_mark_the_field_ignored()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { NoiseSuppression = true });
		Assert.That(connection.IgnoredVoiceSettings, Is.EqualTo(_noiseSuppressionOnly));

		_client.RaiseEvent("VOICE_SETTINGS_UPDATE", """{"noise_suppression":false}""");

		Assert.Multiple(() =>
		{
			Assert.That(connection.State.NoiseSuppression, Is.False);
			Assert.That(connection.IgnoredVoiceSettings,
				Is.EqualTo(_noiseSuppressionOnly),
				"an unrelated push must not clear or add to the ignored set");
		});
	}

	[Test]
	public async Task A_rejection_does_not_mark_the_field_ignored()
	{
		_client.Fails("SET_VOICE_SETTINGS", new DiscordRpcException(4000, "Invalid payload"));
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { NoiseSuppression = true });

		Assert.That(connection.HasIgnoredVoiceSettings, Is.False);
	}

	[Test]
	public async Task A_mid_session_auth_rejection_sets_needs_reauthorization()
	{
		_client.Fails("SET_VOICE_SETTINGS", new DiscordRpcException(4009, "Invalid token"));
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		var result = await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { Mute = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(DiscordVoiceSettingsOutcome.Rejected));
			Assert.That(connection.NeedsReauthorization, Is.True);
		});
	}

	[Test]
	public async Task A_timeout_yields_a_failed_result()
	{
		_client.Fails("SET_VOICE_SETTINGS", new TimeoutException("Discord did not answer in time."));
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		var result = await connection.SetVoiceSettingsAsync(new DiscordVoiceSettingsPatch { Mute = true });

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(DiscordVoiceSettingsOutcome.Failed));
			Assert.That(result.Message, Does.Contain("did not answer"));
		});
	}

	[Test]
	public async Task Set_activity_still_swallows_its_failures()
	{
		_client.Fails("SET_ACTIVITY", new DiscordRpcException(4000, "Invalid payload"));
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		Assert.DoesNotThrowAsync(() => connection.SetActivityAsync(activity: null));
	}

	[Test]
	public async Task Guilds_and_channels_are_filtered_to_the_requested_types()
	{
		_client.Responds("GET_CHANNELS",
			"""
			{"channels":[
				{"id":"1","name":"general","type":0},
				{"id":"2","name":"Voice","type":2},
				{"id":"3","name":"Stage","type":13}
			]}
			""");
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		var guilds = await connection.GetGuildsAsync();
		var voice = await connection.GetChannelsAsync("999", _voiceChannelTypes);

		Assert.Multiple(() =>
		{
			Assert.That(guilds.Select(g => g.Name), Is.EqualTo(_myServer));
			Assert.That(voice.Select(c => c.Name), Is.EqualTo(_voiceAndStage));
		});
	}

	[Test]
	public async Task A_failed_directory_read_answers_with_an_empty_list()
	{
		using var connection = Create();
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		_client.Fails("GET_GUILDS", new DiscordRpcException(4000, "nope"));
		var guilds = await connection.GetGuildsAsync();

		Assert.That(guilds, Is.Empty);
	}

	[Test]
	public async Task Disposing_clears_the_state_and_does_not_reconnect()
	{
		var connection = Create(reconnectDelay: TimeSpan.FromMilliseconds(20));
		connection.Start();
		await WaitForAsync(() => connection.State.IsConnected);

		connection.Dispose();
		await Task.Delay(80);

		Assert.Multiple(() =>
		{
			Assert.That(connection.State, Is.EqualTo(DiscordState.Disconnected));
			Assert.That(_client.IsDisposed, Is.True);
		});
	}

	private DiscordConnection Create(DateTimeOffset? expiresAt = null, TimeSpan? reconnectDelay = null)
		=> new(() => _client,
			_oauth,
			"123456",
			"secret",
			new DiscordTokens("stored-access", "stored-refresh", expiresAt, "rpc rpc.voice.read rpc.voice.write"),
			(tokens, _) =>
			{
				_persisted.Add(tokens);
				return Task.CompletedTask;
			},
			events: null,
			reconnectDelay ?? TimeSpan.FromMinutes(5));

	private static async Task WaitForAsync(Func<bool> condition)
	{
		for (var attempt = 0; attempt < 300 && !condition(); attempt++)
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, "The awaited condition did not become true in time.");
	}
}
