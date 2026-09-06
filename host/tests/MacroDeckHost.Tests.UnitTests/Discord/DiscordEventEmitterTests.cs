using System.Text.Json;
using MacroDeckHost.Integrations.Discord;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordEventEmitterTests
{
	private static readonly string[] _connectedOnly = ["connected"];
	private static readonly string[] _disconnectedOnly = ["disconnected"];
	private static readonly string[] _muteThenUnmute = ["muted", "microphone-silenced", "unmuted", "microphone-live"];
	private static readonly string[] _deafenedThenSilenced = ["deafened", "microphone-silenced"];

	private static readonly string[] _serverMuteDeafenUnmute =
		["server-muted", "microphone-silenced", "server-deafened", "server-unmuted"];

	private static readonly string[] _serverDeafenedThenSilenced = ["server-deafened", "microphone-silenced"];
	private static readonly string[] _mutedOnly = ["muted"];
	private static readonly string[] _undeafenedOnly = ["undeafened"];
	private static readonly string[] _undeafenedThenLive = ["undeafened", "microphone-live"];

	private static readonly string[] _serverUnmutedThenLiveThenLeft =
		["server-unmuted", "microphone-live", "voice-channel-left"];

	private static readonly string[] _serverMutedThenSilencedThenJoined =
		["server-muted", "microphone-silenced", "voice-channel-joined"];

	private static readonly string[] _joinedOnly = ["voice-channel-joined"];
	private static readonly string[] _leftOnly = ["voice-channel-left"];
	private static readonly string[] _leftThenJoined = ["voice-channel-left", "voice-channel-joined"];
	private static readonly string[] _connectionStateOnly = ["voice-connection-state-changed"];
	private static readonly string[] _speakingStartThenStop = ["speaking-started", "speaking-stopped"];
	private static readonly string[] _notificationOnly = ["notification-received"];

	private RecordingPublisher _publisher = null!;
	private DiscordEventEmitter _emitter = null!;

	[SetUp]
	public void SetUp()
	{
		_publisher = new RecordingPublisher();
		_emitter = new DiscordEventEmitter(_publisher);
	}

	[Test]
	public void The_first_snapshot_reports_the_connection_but_not_the_state_it_found()
	{
		_emitter.Observe(Connected(selfMuted: true, selfDeafened: true, channelId: "1", channelName: "General"));

		Assert.That(_publisher.Ids, Is.EqualTo(_connectedOnly));
	}

	[Test]
	public void Muting_and_unmuting_report_separately()
	{
		Seed(Connected());

		_emitter.Observe(Connected(selfMuted: true));
		_emitter.Observe(Connected());

		Assert.That(_publisher.Ids, Is.EqualTo(_muteThenUnmute));
	}

	[Test]
	public void Deafening_reports_a_deafen_event_not_a_mute_one()
	{
		Seed(Connected());

		_emitter.Observe(Connected(selfDeafened: true));

		Assert.That(_publisher.Ids, Is.EqualTo(_deafenedThenSilenced));
	}

	[Test]
	public void A_moderator_deafen_silences_the_microphone()
	{
		Seed(Connected());

		_emitter.Observe(Connected(serverDeafened: true));

		Assert.That(_publisher.Ids, Is.EqualTo(_serverDeafenedThenSilenced));
	}

	[Test]
	public void Muting_while_already_deafened_reports_only_the_raw_muted_event()
	{
		Seed(Connected(selfDeafened: true));

		_emitter.Observe(Connected(selfMuted: true, selfDeafened: true));

		Assert.That(_publisher.Ids, Is.EqualTo(_mutedOnly));
	}

	[Test]
	public void Undeafening_while_still_self_muted_does_not_report_the_microphone_live()
	{
		Seed(Connected(selfMuted: true, selfDeafened: true));

		_emitter.Observe(Connected(selfMuted: true));

		Assert.That(_publisher.Ids, Is.EqualTo(_undeafenedOnly));
	}

	[Test]
	public void Undeafening_reports_the_microphone_live_when_nothing_else_mutes_it()
	{
		Seed(Connected(selfDeafened: true));

		_emitter.Observe(Connected());

		Assert.That(_publisher.Ids, Is.EqualTo(_undeafenedThenLive));
	}

	[Test]
	public void Leaving_a_channel_while_server_muted_reports_the_unmute_before_the_leave()
	{
		Seed(Connected(serverMuted: true, channelId: "1", channelName: "General"));

		_emitter.Observe(Connected());

		Assert.That(_publisher.Ids, Is.EqualTo(_serverUnmutedThenLiveThenLeft));
	}

	[Test]
	public void Joining_a_channel_while_server_muted_reports_the_mute_before_the_join()
	{
		Seed(Connected());

		_emitter.Observe(Connected(serverMuted: true, channelId: "1", channelName: "General"));

		Assert.That(_publisher.Ids, Is.EqualTo(_serverMutedThenSilencedThenJoined));
	}

	[Test]
	public void Reconnecting_into_a_deafened_client_reports_only_the_connection()
	{
		_emitter.Observe(Connected(selfDeafened: true));

		Assert.That(_publisher.Ids, Is.EqualTo(_connectedOnly));
	}

	[Test]
	public void Server_mute_and_self_mute_are_distinct_events()
	{
		Seed(Connected());

		_emitter.Observe(Connected(serverMuted: true));
		_emitter.Observe(Connected(serverMuted: true, serverDeafened: true));
		_emitter.Observe(Connected(serverDeafened: true));

		Assert.That(_publisher.Ids, Is.EqualTo(_serverMuteDeafenUnmute));
	}

	[Test]
	public void Joining_a_channel_reports_its_name_and_server()
	{
		Seed(Connected());

		_emitter.Observe(Connected(channelId: "555", channelName: "General", guildName: "My Server"));

		Assert.That(_publisher.Ids, Is.EqualTo(_joinedOnly));
		var parameters = _publisher.Published[0].Parameters!;
		Assert.Multiple(() =>
		{
			Assert.That(parameters["channelName"], Is.EqualTo("General"));
			Assert.That(parameters["channelId"], Is.EqualTo("555"));
			Assert.That(parameters["guildName"], Is.EqualTo("My Server"));
		});
	}

	[Test]
	public void Leaving_a_channel_reports_the_channel_that_was_left()
	{
		Seed(Connected(channelId: "555", channelName: "General"));

		_emitter.Observe(Connected());

		Assert.That(_publisher.Ids, Is.EqualTo(_leftOnly));
		Assert.That(_publisher.Published[0].Parameters!["channelName"], Is.EqualTo("General"));
	}

	[Test]
	public void Switching_channels_reports_a_leave_then_a_join()
	{
		Seed(Connected(channelId: "1", channelName: "First"));

		_emitter.Observe(Connected(channelId: "2", channelName: "Second"));

		Assert.That(_publisher.Ids, Is.EqualTo(_leftThenJoined));
	}

	[Test]
	public void Losing_the_connection_reports_a_disconnect()
	{
		Seed(Connected(selfMuted: true, channelId: "1"));

		_emitter.Observe(DiscordState.Disconnected);

		Assert.That(_publisher.Ids, Is.EqualTo(_disconnectedOnly));
	}

	[Test]
	public void Reconnecting_does_not_replay_the_state_it_finds()
	{
		Seed(Connected());
		_emitter.Observe(DiscordState.Disconnected);
		_emitter.Reset();
		_publisher.Published.Clear();

		_emitter.Observe(Connected(selfMuted: true, selfDeafened: true, channelId: "1", channelName: "General"));

		Assert.That(_publisher.Ids, Is.EqualTo(_connectedOnly));
	}

	[Test]
	public void Nothing_is_reported_while_disconnected()
	{
		Seed(DiscordState.Disconnected);

		_emitter.Observe(DiscordState.Disconnected with { SelfMuted = true });

		Assert.That(_publisher.Published, Is.Empty);
	}

	[Test]
	public void A_voice_connection_state_change_carries_the_state_and_ping()
	{
		Seed(Connected());

		_emitter.Observe(Connected() with { VoiceConnectionState = "NO_ROUTE", AveragePing = 0 });

		Assert.That(_publisher.Ids, Is.EqualTo(_connectionStateOnly));
		Assert.That(_publisher.Published[0].Parameters!["state"], Is.EqualTo("NO_ROUTE"));
	}

	[Test]
	public void Speaking_is_published_directly_with_the_speaker()
	{
		_emitter.PublishSpeaking("42", isSelf: true, speaking: true);
		_emitter.PublishSpeaking("43", isSelf: false, speaking: false);

		Assert.That(_publisher.Ids, Is.EqualTo(_speakingStartThenStop));
		Assert.Multiple(() =>
		{
			Assert.That(_publisher.Published[0].Parameters!["userId"], Is.EqualTo("42"));
			Assert.That(_publisher.Published[0].Parameters!["isSelf"], Is.EqualTo(true));
			Assert.That(_publisher.Published[1].Parameters!["isSelf"], Is.EqualTo(false));
		});
	}

	[Test]
	public void A_notification_is_flattened_into_payload_parameters()
	{
		using var document = JsonDocument.Parse(
			"""{"title":"Someone","body":"hey","channel_id":"9","icon_url":"https://example.invalid/a.png"}""");

		_emitter.PublishNotification(document.RootElement);

		Assert.That(_publisher.Ids, Is.EqualTo(_notificationOnly));
		var parameters = _publisher.Published[0].Parameters!;
		Assert.Multiple(() =>
		{
			Assert.That(parameters["title"], Is.EqualTo("Someone"));
			Assert.That(parameters["body"], Is.EqualTo("hey"));
			Assert.That(parameters["channelId"], Is.EqualTo("9"));
			Assert.That(parameters["iconUrl"], Is.EqualTo("https://example.invalid/a.png"));
		});
	}

	[Test]
	public void A_notification_with_missing_fields_still_publishes()
	{
		using var document = JsonDocument.Parse("""{"title":"Someone"}""");

		_emitter.PublishNotification(document.RootElement);

		Assert.That(_publisher.Published[0].Parameters!["body"], Is.EqualTo(string.Empty));
	}

	private static DiscordState Connected(
		bool selfMuted = false,
		bool selfDeafened = false,
		bool serverMuted = false,
		bool serverDeafened = false,
		string? channelId = null,
		string? channelName = null,
		string? guildName = null) => new()
	{
		IsConnected = true,
		UserId = "me",
		SelfMuted = selfMuted,
		SelfDeafened = selfDeafened,
		ServerMuted = serverMuted,
		ServerDeafened = serverDeafened,
		VoiceChannelId = channelId,
		VoiceChannelName = channelName,
		VoiceGuildName = guildName
	};

	private void Seed(DiscordState state)
	{
		_emitter.Observe(state);
		_publisher.Published.Clear();
	}

	private sealed class RecordingPublisher : IEventPublisher
	{
		public List<(string EventId, IReadOnlyDictionary<string, object?>? Parameters)> Published { get; } = [];

		public IEnumerable<string> Ids => Published.Select(p => p.EventId);

		public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
			=> Published.Add((eventId, parameters));
	}
}
