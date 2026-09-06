using System.Text.Json;
using MacroDeckHost.Integrations.Discord;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordStateMapperTests
{
	private static readonly DiscordState _connected = new() { IsConnected = true, UserId = "me" };
	private static readonly string[] _grantedScopes = ["rpc", "rpc.voice.read"];

	[Test]
	public void Voice_settings_are_read_in_full()
	{
		var state = DiscordStateMapper.ApplyVoiceSettings(_connected,
			Json("""
				 {
				 	"mute": true,
				 	"deaf": true,
				 	"noise_suppression": true,
				 	"echo_cancellation": false,
				 	"automatic_gain_control": true,
				 	"input": { "volume": 73.5 },
				 	"output": { "volume": 145 },
				 	"mode": { "type": "PUSH_TO_TALK" }
				 }
				 """));

		Assert.Multiple(() =>
		{
			Assert.That(state.SelfMuted, Is.True);
			Assert.That(state.SelfDeafened, Is.True);
			Assert.That(state.NoiseSuppression, Is.True);
			Assert.That(state.EchoCancellation, Is.False);
			Assert.That(state.AutomaticGainControl, Is.True);
			Assert.That(state.InputVolume, Is.EqualTo(73.5));
			Assert.That(state.OutputVolume, Is.EqualTo(145));
			Assert.That(state.VoiceMode, Is.EqualTo("PUSH_TO_TALK"));
		});
	}

	[Test]
	public void A_deaf_true_mute_false_payload_silences_the_microphone()
	{
		var state = DiscordStateMapper.ApplyVoiceSettings(_connected, Json("""{"mute":false,"deaf":true}"""));

		Assert.Multiple(() =>
		{
			Assert.That(state.SelfMuted, Is.False);
			Assert.That(state.SelfDeafened, Is.True);
			Assert.That(state.EffectivelyMuted, Is.True);
		});
	}

	[Test]
	public void A_partial_voice_settings_payload_leaves_the_other_values_alone()
	{
		var seeded = _connected with { SelfMuted = true, NoiseSuppression = true, InputVolume = 80 };

		var state = DiscordStateMapper.ApplyVoiceSettings(seeded, Json("""{"deaf":true}"""));

		Assert.Multiple(() =>
		{
			Assert.That(state.SelfDeafened, Is.True);
			Assert.That(state.SelfMuted, Is.True);
			Assert.That(state.NoiseSuppression, Is.True);
			Assert.That(state.InputVolume, Is.EqualTo(80));
		});
	}

	[Test]
	public void Our_own_voice_state_supplies_the_server_flags()
	{
		var state = DiscordStateMapper.ApplyVoiceState(_connected,
			Json("""
				 {
				 	"user": { "id": "me" },
				 	"voice_state": { "mute": true, "deaf": true, "self_mute": false, "self_deaf": false }
				 }
				 """));

		Assert.Multiple(() =>
		{
			Assert.That(state.ServerMuted, Is.True);
			Assert.That(state.ServerDeafened, Is.True);
			Assert.That(state.EffectivelyMuted, Is.True, "the combined value has to follow the server flag");
			Assert.That(state.Deafened, Is.True);
		});
	}

	[Test]
	public void Our_own_voice_state_does_not_touch_the_self_flags()
	{
		var seeded = _connected with { SelfMuted = true, SelfDeafened = true };

		var state = DiscordStateMapper.ApplyVoiceState(seeded,
			Json("""{"user":{"id":"me"},"voice_state":{"self_mute":false,"self_deaf":false}}"""));

		Assert.Multiple(() =>
		{
			Assert.That(state.SelfMuted, Is.True);
			Assert.That(state.SelfDeafened, Is.True);
		});
	}

	[Test]
	public void Someone_elses_voice_state_is_ignored()
	{
		var state = DiscordStateMapper.ApplyVoiceState(_connected,
			Json("""{"user":{"id":"someone-else"},"voice_state":{"mute":true,"deaf":true}}"""));

		Assert.Multiple(() =>
		{
			Assert.That(state.ServerMuted, Is.False);
			Assert.That(state.ServerDeafened, Is.False);
		});
	}

	[Test]
	public void A_selected_channel_brings_its_name_server_and_own_voice_state()
	{
		var state = DiscordStateMapper.ApplySelectedVoiceChannel(_connected,
			Json("""
				 {
				 	"id": "555",
				 	"name": "General",
				 	"guild_id": "999",
				 	"voice_states": [
				 		{ "user": { "id": "other" }, "voice_state": { "mute": false, "deaf": false } },
				 		{ "user": { "id": "me" }, "voice_state": { "mute": true, "deaf": false } }
				 	]
				 }
				 """),
			guildName: "My Server");

		Assert.Multiple(() =>
		{
			Assert.That(state.VoiceChannelId, Is.EqualTo("555"));
			Assert.That(state.VoiceChannelName, Is.EqualTo("General"));
			Assert.That(state.VoiceGuildId, Is.EqualTo("999"));
			Assert.That(state.VoiceGuildName, Is.EqualTo("My Server"));
			Assert.That(state.InVoiceChannel, Is.True);
			Assert.That(state.ServerMuted, Is.True);
			Assert.That(state.ServerDeafened, Is.False);
		});
	}

	[Test]
	public void A_new_channel_clears_the_server_flags_of_the_previous_one()
	{
		var seeded = _connected with { ServerMuted = true, ServerDeafened = true, VoiceChannelId = "old" };

		var state = DiscordStateMapper.ApplySelectedVoiceChannel(seeded,
			Json("""{"id":"new","name":"Other","guild_id":"999","voice_states":[]}"""));

		Assert.Multiple(() =>
		{
			Assert.That(state.VoiceChannelId, Is.EqualTo("new"));
			Assert.That(state.ServerMuted, Is.False);
			Assert.That(state.ServerDeafened, Is.False);
		});
	}

	[Test]
	public void A_null_selected_channel_means_not_in_a_voice_channel()
	{
		var seeded = _connected with
		{
			VoiceChannelId = "555",
			VoiceChannelName = "General",
			VoiceGuildName = "My Server",
			ServerMuted = true,
			SelfSpeaking = true,
			AveragePing = 42
		};

		var state = DiscordStateMapper.ApplySelectedVoiceChannel(seeded, Json("null"));

		Assert.Multiple(() =>
		{
			Assert.That(state.InVoiceChannel, Is.False);
			Assert.That(state.VoiceChannelName, Is.Null);
			Assert.That(state.VoiceGuildName, Is.Null);
			Assert.That(state.ServerMuted, Is.False);
			Assert.That(state.SelfSpeaking, Is.False);
			Assert.That(state.AveragePing, Is.Null);
		});
	}

	[Test]
	public void Leaving_a_channel_keeps_the_client_wide_settings()
	{
		var seeded = _connected with { SelfMuted = true, SelfDeafened = true, InputVolume = 55 };

		var state = seeded.WithoutVoiceChannel();

		Assert.Multiple(() =>
		{
			Assert.That(state.SelfMuted, Is.True);
			Assert.That(state.SelfDeafened, Is.True);
			Assert.That(state.InputVolume, Is.EqualTo(55));
		});
	}

	[Test]
	public void The_voice_connection_status_supplies_the_state_and_ping()
	{
		var state = DiscordStateMapper.ApplyVoiceConnectionStatus(_connected,
			Json("""{"state":"VOICE_CONNECTED","average_ping":37}"""));

		Assert.Multiple(() =>
		{
			Assert.That(state.VoiceConnectionState, Is.EqualTo("VOICE_CONNECTED"));
			Assert.That(state.AveragePing, Is.EqualTo(37));
		});
	}

	[Test]
	public void The_user_is_read_from_a_ready_or_authenticate_payload()
	{
		var (id, name) = DiscordStateMapper.ReadUser(
			Json("""{"user":{"id":"7","username":"legacy","global_name":"Display Name"}}"""));

		Assert.Multiple(() =>
		{
			Assert.That(id, Is.EqualTo("7"));
			Assert.That(name, Is.EqualTo("Display Name"), "the display name is preferred over the handle");
		});
	}

	[Test]
	public void The_username_is_used_when_no_display_name_is_set()
	{
		var (_, name) = DiscordStateMapper.ReadUser(Json("""{"user":{"id":"7","username":"legacy"}}"""));

		Assert.That(name, Is.EqualTo("legacy"));
	}

	[Test]
	public void Granted_scopes_are_read_from_the_authenticate_payload()
	{
		var scopes = DiscordStateMapper.ReadScopes(Json("""{"scopes":["rpc","rpc.voice.read"]}"""));

		Assert.That(scopes, Is.EqualTo(_grantedScopes));
	}

	[Test]
	public void An_unexpected_payload_shape_is_survivable()
	{
		Assert.Multiple(() =>
		{
			Assert.That(DiscordStateMapper.ApplyVoiceSettings(_connected, Json("[]")), Is.EqualTo(_connected));
			Assert.That(DiscordStateMapper.ApplyVoiceState(_connected, Json("42")), Is.EqualTo(_connected));
			Assert.That(DiscordStateMapper.ReadScopes(Json("\"nope\"")), Is.Empty);
			Assert.That(DiscordStateMapper.ReadUser(Json("null")).Id, Is.Null);
		});
	}

	private static JsonElement Json(string json)
	{
		using var document = JsonDocument.Parse(json);
		return document.RootElement.Clone();
	}
}
