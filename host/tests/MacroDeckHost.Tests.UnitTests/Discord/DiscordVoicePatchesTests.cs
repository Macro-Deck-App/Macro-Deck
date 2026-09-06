using System.Text.Json;
using MacroDeckHost.Integrations.Discord;
using MacroDeckHost.Integrations.Discord.Actions;
using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordVoicePatchesTests
{
	[Test]
	public void Deafening_never_writes_a_mute_value()
	{
		var patch = DiscordVoicePatches.Deafen(target: true);

		Assert.That(Serialize(patch), Is.EqualTo("""{"deaf":true}"""));
	}

	[Test]
	public void Undeafening_never_writes_a_mute_value()
	{
		var patch = DiscordVoicePatches.Deafen(target: false);

		Assert.That(Serialize(patch), Is.EqualTo("""{"deaf":false}"""));
	}

	[Test]
	public void Muting_writes_only_the_mute_field()
	{
		var patch = DiscordVoicePatches.Mute(target: true, State(selfDeafened: false));

		Assert.That(Serialize(patch), Is.EqualTo("""{"mute":true}"""));
	}

	[Test]
	public void Muting_while_deafened_still_writes_only_the_mute_field()
	{
		var patch = DiscordVoicePatches.Mute(target: true, State(selfDeafened: true));

		Assert.That(Serialize(patch), Is.EqualTo("""{"mute":true}"""));
	}

	[Test]
	public void Unmuting_while_not_deafened_writes_only_the_mute_field()
	{
		var patch = DiscordVoicePatches.Mute(target: false, State(selfDeafened: false));

		Assert.That(Serialize(patch), Is.EqualTo("""{"mute":false}"""));
	}

	[Test]
	public void Unmuting_while_deafened_lifts_the_deafen_too()
	{
		var patch = DiscordVoicePatches.Mute(target: false, State(selfDeafened: true));

		Assert.That(Serialize(patch), Is.EqualTo("""{"mute":false,"deaf":false}"""));
	}

	[Test]
	public void A_server_deafen_alone_does_not_add_a_deafen_field()
	{
		var state = new DiscordState { IsConnected = true, ServerDeafened = true };

		var patch = DiscordVoicePatches.Mute(target: false, state);

		Assert.That(Serialize(patch), Is.EqualTo("""{"mute":false}"""));
	}

	[Test]
	public void Toggling_resolves_against_the_current_value()
	{
		Assert.Multiple(() =>
		{
			Assert.That(DiscordActionParameters.ResolveState("toggle", current: true), Is.False);
			Assert.That(DiscordActionParameters.ResolveState("toggle", current: false), Is.True);
			Assert.That(DiscordActionParameters.ResolveState("on", current: true), Is.True);
			Assert.That(DiscordActionParameters.ResolveState("off", current: false), Is.False);
		});
	}

	[Test]
	public void A_missing_selection_toggles()
	{
		Assert.Multiple(() =>
		{
			Assert.That(DiscordActionParameters.ResolveState(null, current: true), Is.False);
			Assert.That(DiscordActionParameters.ResolveState(42, current: false), Is.True);
		});
	}

	private static DiscordState State(bool selfDeafened)
		=> new() { IsConnected = true, SelfDeafened = selfDeafened };

	private static string Serialize(DiscordVoiceSettingsPatch patch)
		=> JsonSerializer.Serialize(patch, DiscordRpcClient.SerializerOptions);
}
