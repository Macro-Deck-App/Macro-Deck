using MacroDeckHost.Integrations.Twitch;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchVariablesTests
{
	private static readonly TwitchAccount _account = new(Guid.NewGuid(),
		"client-id",
		"111",
		"streamer",
		"Streamer",
		"streamer",
		DateTimeOffset.UtcNow);

	[Test]
	public void Variables_are_namespaced_by_the_login()
	{
		var declared = TwitchVariables.Declare("streamer").Select(variable => variable.Name).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(declared, Has.Member("twitch_streamer_is_live"));
			Assert.That(declared, Has.Count.EqualTo(TwitchVariables.Names.Count));
			Assert.That(declared, Is.Unique);
		});
	}

	[Test]
	public void Two_accounts_do_not_collide()
	{
		var first = TwitchVariables.Declare("streamer").Select(variable => variable.Name);
		var second = TwitchVariables.Declare("botaccount").Select(variable => variable.Name);

		Assert.That(first.Intersect(second, StringComparer.Ordinal), Is.Empty);
	}

	[TestCase("twitch_streamer_is_live", "streamer", "is_live")]
	[TestCase("twitch_my_bot_is_live", "my_bot", "is_live")]
	[TestCase("twitch_my_bot_slow_mode_wait_time", "my_bot", "slow_mode_wait_time")]
	public void A_variable_name_splits_into_its_account_and_its_field(string variable, string key, string name)
	{
		var split = TwitchVariables.Split(variable);

		Assert.Multiple(() =>
		{
			Assert.That(split!.Value.VariableKey, Is.EqualTo(key));
			Assert.That(split.Value.Name, Is.EqualTo(name));
		});
	}

	[TestCase("nonsense")]
	[TestCase("twitch_streamer")]
	[TestCase("twitch_is_live")]
	public void An_unrecognised_name_does_not_split(string variable)
	{
		Assert.That(TwitchVariables.Split(variable), Is.Null);
	}

	[Test]
	public void Everything_but_the_connection_flag_is_unknown_before_anything_was_read()
	{
		Assert.Multiple(() =>
		{
			Assert.That(TwitchVariables.Read(_account, TwitchAccountState.Unknown, "is_connected"), Is.False);

			foreach (var name in TwitchVariables.Names.Except(["is_connected", "display_name", "login"],
				StringComparer.Ordinal))
			{
				Assert.That(TwitchVariables.Read(_account, TwitchAccountState.Unknown, name), Is.Null, name);
			}
		});
	}

	[Test]
	public void The_account_identity_answers_even_while_offline()
	{
		Assert.Multiple(() =>
		{
			Assert.That(TwitchVariables.Read(_account, TwitchAccountState.Unknown, "login"), Is.EqualTo("streamer"));
			Assert.That(TwitchVariables.Read(_account, TwitchAccountState.Unknown, "display_name"),
				Is.EqualTo("Streamer"));
		});
	}

	[Test]
	public void A_known_state_is_read_field_by_field()
	{
		var state = TwitchAccountState.Unknown with
		{
			IsConnected = true,
			IsLive = true,
			ViewerCount = 120,
			StreamTitle = "Playing Hades",
			StreamCategory = "Hades",
			FollowerCount = 4200,
			SubscriberCount = 12,
			SubscriberPoints = 20,
			StreamStartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
			ChatSettings = new TwitchChatSettings(true, false, null, true, 30, false, true),
			LastFollower = "NewFan"
		};

		Assert.Multiple(() =>
		{
			Assert.That(TwitchVariables.Read(_account, state, "viewer_count"), Is.EqualTo(120));
			Assert.That(TwitchVariables.Read(_account, state, "stream_title"), Is.EqualTo("Playing Hades"));
			Assert.That(TwitchVariables.Read(_account, state, "follower_count"), Is.EqualTo(4200));
			Assert.That(TwitchVariables.Read(_account, state, "emote_only"), Is.True);
			Assert.That(TwitchVariables.Read(_account, state, "slow_mode_wait_time"), Is.EqualTo(30));
			Assert.That(TwitchVariables.Read(_account, state, "last_follower"), Is.EqualTo("NewFan"));
			Assert.That(TwitchVariables.Read(_account, state, "uptime_seconds"), Is.GreaterThan(500));
		});
	}

	[Test]
	public void Uptime_is_unknown_while_the_channel_is_offline()
	{
		var state = TwitchAccountState.Unknown with { IsLive = false, StreamStartedAt = DateTimeOffset.UtcNow };

		Assert.That(TwitchVariables.Read(_account, state, "uptime_seconds"), Is.Null);
	}

	[Test]
	public void An_unknown_field_answers_null()
	{
		Assert.That(TwitchVariables.Read(_account, TwitchAccountState.Unknown, "nonsense"), Is.Null);
	}

	[Test]
	public void Every_declared_variable_has_a_reader()
	{
		var state = TwitchAccountState.Unknown with { IsConnected = true };

		Assert.Multiple(() =>
		{
			foreach (var declared in TwitchVariables.Declare("streamer"))
			{
				var split = TwitchVariables.Split(declared.Name!);
				Assert.That(split, Is.Not.Null, declared.Name);
				Assert.That(TwitchVariables.Names, Has.Member(split!.Value.Name), declared.Name);
				Assert.DoesNotThrow(() => TwitchVariables.Read(_account, state, split.Value.Name));
			}
		});
	}
}
