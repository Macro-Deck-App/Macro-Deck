using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Steam.Protocol;
using MacroDeckHost.Integrations.Steam.State;
using MacroDeckHost.Integrations.Steam.Variables;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamAccountDataTests
{
	private const string SteamId = "76561198000000001";

	private FakeTimeProvider _time = null!;
	private ScriptedWebApi _web = null!;

	[SetUp]
	public void SetUp()
	{
		_time = new FakeTimeProvider();
		_web = new ScriptedWebApi { Self = new SteamPlayerSummary(SteamId, "Me", SteamPersona.Online, null, null) };
	}

	[Test]
	public async Task Owned_games_give_the_library_variables()
	{
		_web.OwnedGames =
		[
			new SteamOwnedGame(730, "Counter-Strike 2", 6000, 90, DateTimeOffset.FromUnixTimeSeconds(200)),
			new SteamOwnedGame(220, "Half-Life 2", 630, 0, DateTimeOffset.FromUnixTimeSeconds(100))
		];
		var tracker = await ConnectedAsync();

		await tracker.PollOwnedGamesAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Read(tracker, SteamVariables.GamesOwned).Value, Is.EqualTo(2));
			Assert.That(Read(tracker, SteamVariables.TotalPlaytimeHours).Value, Is.EqualTo(110.5));
			Assert.That(Read(tracker, SteamVariables.RecentPlaytimeHours).Value, Is.EqualTo(1.5));
		});
	}

	[Test]
	public async Task Private_game_details_leave_the_library_variables_unavailable()
	{
		_web.OwnedGames = null;
		var tracker = await ConnectedAsync();

		await tracker.PollOwnedGamesAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(tracker.Snapshot.Accounts[0].Owned, Is.EqualTo(SteamOwnedState.Private));
			Assert.That(Read(tracker, SteamVariables.GamesOwned), Is.EqualTo(VariableReading.Unavailable));
			Assert.That(Read(tracker, SteamVariables.TotalPlaytimeHours), Is.EqualTo(VariableReading.Unavailable));
		});
	}

	[Test]
	public async Task A_library_with_no_games_is_an_empty_library_not_a_private_one()
	{
		_web.OwnedGames = [];
		var tracker = await ConnectedAsync();

		await tracker.PollOwnedGamesAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(tracker.Snapshot.Accounts[0].Owned, Is.EqualTo(SteamOwnedState.Available));
			Assert.That(Read(tracker, SteamVariables.GamesOwned).Value, Is.EqualTo(0));
		});
	}

	[Test]
	public async Task A_short_outage_keeps_the_last_known_library()
	{
		_web.OwnedGames = [new SteamOwnedGame(730, "Counter-Strike 2", 60, 0, null)];
		var tracker = await ConnectedAsync();
		await tracker.PollOwnedGamesAsync(CancellationToken.None);

		_web.OwnedFailure = SteamWebFailure.Unavailable;
		await tracker.PollOwnedGamesAsync(CancellationToken.None);

		Assert.That(Read(tracker, SteamVariables.GamesOwned).Value, Is.EqualTo(1));
	}

	[Test]
	public async Task Without_a_key_there_are_no_library_variables()
	{
		var tracker = new SteamTracker(new FakeSteamPlatform(),
			[new SteamAccountConfig("e1", "Main", "main")],
			[],
			_time,
			_ =>
			{
			});

		await tracker.PollOwnedGamesAsync(CancellationToken.None);

		Assert.That(Read(tracker, SteamVariables.GamesOwned), Is.EqualTo(VariableReading.Unavailable));
	}

	[Test]
	public async Task The_friend_list_is_kept_with_players_first_then_online_then_offline()
	{
		_web.Friends =
		[
			new SteamPlayerSummary("1", "Zed", SteamPersona.Offline, null, null),
			new SteamPlayerSummary("2", "Amy", SteamPersona.Away, null, null),
			new SteamPlayerSummary("3", "Bob", SteamPersona.Online, 730, "Counter-Strike 2"),
			new SteamPlayerSummary("4", "Cid", SteamPersona.Online, null, null)
		];
		var tracker = await ConnectedAsync();

		await tracker.PollFriendsAsync(CancellationToken.None);

		var account = tracker.Snapshot.Accounts[0];
		Assert.Multiple(() =>
		{
			Assert.That(account.FriendList.Select(friend => friend.Name),
				Is.EqualTo(new[] { "Bob", "Cid", "Amy", "Zed" }));
			Assert.That(account.FriendsOnline, Is.EqualTo(3));
			Assert.That(account.FriendsInGame, Is.EqualTo(1));
		});
	}

	private async Task<SteamTracker> ConnectedAsync()
	{
		var tracker = new SteamTracker(new FakeSteamPlatform(),
			[new SteamAccountConfig("e1", "Main", "main", SteamId, _web)],
			[],
			_time,
			_ =>
			{
			});
		await tracker.PollSelfAsync(CancellationToken.None);
		return tracker;
	}

	private static VariableReading Read(SteamTracker tracker, string name)
	{
		return SteamVariables.Read("steam_main_" + name, tracker.Snapshot, DateTimeOffset.UnixEpoch);
	}
}
