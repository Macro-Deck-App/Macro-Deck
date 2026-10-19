using MacroDeckHost.Integrations.Discord.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordIpcDenialTests
{
	[Test]
	public void An_elevated_discord_is_a_privilege_mismatch()
	{
		var cause = DiscordIpcDenial.Classify(TestDenials.Server(elevated: true), TestDenials.Self);

		Assert.That(cause, Is.EqualTo(DiscordIpcDenialCause.PrivilegeMismatch));
	}

	[Test]
	public void An_elevated_macro_deck_and_a_normal_discord_is_a_privilege_mismatch()
	{
		var cause = DiscordIpcDenial.Classify(TestDenials.Server(elevated: false),
			TestDenials.Self with { Elevated = true });

		Assert.That(cause, Is.EqualTo(DiscordIpcDenialCause.PrivilegeMismatch));
	}

	[Test]
	public void Discord_with_the_same_privileges_is_blocked_by_something_else()
	{
		var cause = DiscordIpcDenial.Classify(TestDenials.Server(), TestDenials.Self);

		Assert.That(cause, Is.EqualTo(DiscordIpcDenialCause.Blocked));
	}

	[TestCase("DiscordPTB")]
	[TestCase("DiscordCanary")]
	[TestCase("discord")]
	public void Every_discord_build_counts_as_discord(string name)
	{
		var cause = DiscordIpcDenial.Classify(TestDenials.Server(name), TestDenials.Self);

		Assert.That(cause, Is.EqualTo(DiscordIpcDenialCause.Blocked));
	}

	[Test]
	public void Discord_in_another_session_is_reported_as_such()
	{
		var cause = DiscordIpcDenial.Classify(TestDenials.Server(session: 2, elevated: true), TestDenials.Self);

		Assert.That(cause, Is.EqualTo(DiscordIpcDenialCause.OtherSession));
	}

	[Test]
	public void Discord_under_another_user_in_the_same_session_is_reported_as_another_session()
	{
		var cause = DiscordIpcDenial.Classify(TestDenials.Server(userSid: "S-1-5-21-2000"), TestDenials.Self);

		Assert.That(cause, Is.EqualTo(DiscordIpcDenialCause.OtherSession));
	}

	[Test]
	public void Another_program_holding_the_pipe_is_named_whatever_its_session()
	{
		var cause = DiscordIpcDenial.Classify(TestDenials.Server("SomeOverlay", session: 0), TestDenials.Self);

		Assert.That(cause, Is.EqualTo(DiscordIpcDenialCause.OtherProgram));
	}

	[Test]
	public void An_unreadable_elevation_claims_no_cause()
	{
		var cause = DiscordIpcDenial.Classify(TestDenials.Server(elevated: null), TestDenials.Self);

		Assert.That(cause, Is.EqualTo(DiscordIpcDenialCause.Unknown));
	}

	[Test]
	public void A_failed_probe_claims_no_cause()
	{
		var cause = DiscordIpcDenial.Classify(null, TestDenials.Self);

		Assert.That(cause, Is.EqualTo(DiscordIpcDenialCause.Unknown));
	}

	[Test]
	public void A_discord_owned_endpoint_is_preferred()
	{
		var other = DiscordIpcDenial.Create("discord-ipc-0", TestDenials.Server("SomeOverlay"), TestDenials.Self);
		var discord = DiscordIpcDenial.Create("discord-ipc-1", TestDenials.Server(), TestDenials.Self);

		var preferred = DiscordIpcDenial.Prefer([TestDenials.Unattributed(), other, discord]);

		Assert.That(preferred, Is.SameAs(discord));
	}

	[Test]
	public void A_known_owner_is_preferred_over_a_failed_probe()
	{
		var other = DiscordIpcDenial.Create("discord-ipc-1", TestDenials.Server("SomeOverlay"), TestDenials.Self);

		var preferred = DiscordIpcDenial.Prefer([TestDenials.Unattributed(), other]);

		Assert.That(preferred, Is.SameAs(other));
	}
}
