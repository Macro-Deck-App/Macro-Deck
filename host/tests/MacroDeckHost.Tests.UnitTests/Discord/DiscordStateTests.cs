using MacroDeckHost.Integrations.Discord;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordStateTests
{
	[TestCase(false, false, false, false, ExpectedResult = false)]
	[TestCase(true, false, false, false, ExpectedResult = true)]
	[TestCase(false, true, false, false, ExpectedResult = true)]
	[TestCase(false, false, true, false, ExpectedResult = true)]
	[TestCase(false, false, false, true, ExpectedResult = true)]
	[TestCase(true, true, false, false, ExpectedResult = true)]
	[TestCase(true, false, true, false, ExpectedResult = true)]
	[TestCase(true, false, false, true, ExpectedResult = true)]
	[TestCase(false, true, true, false, ExpectedResult = true)]
	[TestCase(false, true, false, true, ExpectedResult = true)]
	[TestCase(false, false, true, true, ExpectedResult = true)]
	[TestCase(true, true, true, false, ExpectedResult = true)]
	[TestCase(true, true, false, true, ExpectedResult = true)]
	[TestCase(true, false, true, true, ExpectedResult = true)]
	[TestCase(false, true, true, true, ExpectedResult = true)]
	[TestCase(true, true, true, true, ExpectedResult = true)]
	public bool EffectivelyMuted_is_the_or_of_all_four_flags(
		bool selfMuted,
		bool selfDeafened,
		bool serverMuted,
		bool serverDeafened)
		=> State(selfMuted, selfDeafened, serverMuted, serverDeafened).EffectivelyMuted;

	[Test]
	public void Self_deafening_with_the_raw_mute_flag_false_silences_the_microphone()
	{
		var state = State(selfMuted: false, selfDeafened: true, serverMuted: false, serverDeafened: false);

		Assert.That(state.EffectivelyMuted, Is.True);
	}

	[Test]
	public void A_moderator_deafen_silences_the_microphone()
	{
		var state = State(selfMuted: false, selfDeafened: false, serverMuted: false, serverDeafened: true);

		Assert.That(state.EffectivelyMuted, Is.True);
	}

	[Test]
	public void Undeafening_leaves_the_microphone_silenced_while_a_raw_self_mute_remains()
	{
		var state = State(selfMuted: true, selfDeafened: false, serverMuted: false, serverDeafened: false);

		Assert.That(state.EffectivelyMuted, Is.True);
	}

	[Test]
	public void Undeafening_leaves_the_microphone_silenced_while_a_raw_server_mute_remains()
	{
		var state = State(selfMuted: false, selfDeafened: false, serverMuted: true, serverDeafened: false);

		Assert.That(state.EffectivelyMuted, Is.True);
	}

	[Test]
	public void Undeafening_reports_the_microphone_live_when_no_raw_mute_remains()
	{
		var state = State(selfMuted: false, selfDeafened: false, serverMuted: false, serverDeafened: false);

		Assert.That(state.EffectivelyMuted, Is.False);
	}

	[Test]
	public void The_raw_flags_never_follow_the_effective_state()
	{
		var state = State(selfMuted: false, selfDeafened: true, serverMuted: false, serverDeafened: false);

		Assert.Multiple(() =>
		{
			Assert.That(state.SelfMuted, Is.False);
			Assert.That(state.ServerMuted, Is.False);
		});
	}

	[Test]
	public void Deafened_still_means_the_local_setting_or_a_moderator()
	{
		Assert.Multiple(() =>
		{
			Assert.That(State(false, true, false, false).Deafened, Is.True);
			Assert.That(State(false, false, false, true).Deafened, Is.True);
			Assert.That(State(false, false, false, false).Deafened, Is.False);
		});
	}

	[TestCase(false, false, false, false, ExpectedResult = false)]
	[TestCase(true, false, false, false, ExpectedResult = true)]
	[TestCase(false, true, false, false, ExpectedResult = true)]
	[TestCase(false, false, true, false, ExpectedResult = false)]
	[TestCase(false, false, false, true, ExpectedResult = false)]
	[TestCase(true, true, false, false, ExpectedResult = true)]
	public bool EffectivelySelfMuted_ignores_the_moderator_flags(
		bool selfMuted,
		bool selfDeafened,
		bool serverMuted,
		bool serverDeafened)
		=> State(selfMuted, selfDeafened, serverMuted, serverDeafened).EffectivelySelfMuted;

	[Test]
	public void WithoutVoiceChannel_clears_the_moderator_flags_out_of_the_effective_state()
	{
		var state = State(selfMuted: false, selfDeafened: false, serverMuted: true, serverDeafened: true)
			.WithoutVoiceChannel();

		Assert.That(state.EffectivelyMuted, Is.False);
	}

	[Test]
	public void WithoutVoiceChannel_stays_effectively_muted_when_also_self_muted()
	{
		var state = State(selfMuted: true, selfDeafened: false, serverMuted: true, serverDeafened: true)
			.WithoutVoiceChannel();

		Assert.That(state.EffectivelyMuted, Is.True);
	}

	private static DiscordState State(bool selfMuted, bool selfDeafened, bool serverMuted, bool serverDeafened)
		=> new()
		{
			IsConnected = true,
			SelfMuted = selfMuted,
			SelfDeafened = selfDeafened,
			ServerMuted = serverMuted,
			ServerDeafened = serverDeafened
		};
}
