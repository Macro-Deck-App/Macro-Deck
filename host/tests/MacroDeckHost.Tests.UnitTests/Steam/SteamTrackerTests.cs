using MacroDeck.Sdk.Variables;
using MacroDeckHost.Integrations.Steam.Native;
using MacroDeckHost.Integrations.Steam.Protocol;
using MacroDeckHost.Integrations.Steam.State;
using MacroDeckHost.Integrations.Steam.Variables;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Steam;

[TestFixture]
internal sealed class SteamTrackerTests
{
	private const string SteamId = "76561198000000001";

	private SteamFolders _folders = null!;
	private FakeTimeProvider _time = null!;
	private FakeSteamPlatform _platform = null!;
	private FakeWebApi _web = null!;
	private string _steam = null!;
	private readonly List<SteamSnapshot> _published = [];

	[SetUp]
	public void SetUp()
	{
		_folders = new SteamFolders();
		_time = new FakeTimeProvider();
		_steam = _folders.Make("Steam");
		_platform = new FakeSteamPlatform
			{ Os = SteamOs.Windows, HomeDirectory = _folders.Root, RegistrySteamPath = _steam };
		_web = new FakeWebApi();
		_published.Clear();
		SteamFolders.WriteManifest(_steam, 730, "Counter-Strike 2");
		SteamFolders.WriteManifest(_steam, 220, "Half-Life 2");
	}

	[TearDown]
	public void TearDown()
	{
		_folders.Dispose();
	}

	[Test]
	public void With_no_steam_installation_nothing_is_found_and_nothing_is_playing()
	{
		_platform.RegistrySteamPath = null;
		var tracker = Tracker(false);

		tracker.PollLocal();

		Assert.Multiple(() =>
		{
			Assert.That(tracker.Snapshot.SteamFound, Is.False);
			Assert.That(tracker.Snapshot.IsPlaying, Is.False);
			Assert.That(Account(tracker).CurrentGame, Is.Null);
		});
	}

	[Test]
	public void A_running_game_is_named_from_its_manifest()
	{
		_platform.RegistryRunningAppId = 730;
		var tracker = Tracker(false);

		tracker.PollLocal();

		Assert.That(Account(tracker).CurrentGame,
			Has.Property("AppId").EqualTo(730).And.Property("Name").EqualTo("Counter-Strike 2"));
	}

	[Test]
	public void A_running_game_without_a_manifest_is_identified_by_its_id_only()
	{
		_platform.RegistryRunningAppId = 999999;
		var tracker = Tracker(false);

		tracker.PollLocal();

		Assert.That(Account(tracker).CurrentGame!.Name, Is.EqualTo("999999"));
	}

	[Test]
	public void The_session_runs_from_the_first_observation_and_restarts_for_another_game()
	{
		_platform.RegistryRunningAppId = 730;
		var tracker = Tracker(false);
		tracker.PollLocal();
		var started = Account(tracker).CurrentGame!.StartedAt;

		_time.Now = _time.Now.AddMinutes(10);
		tracker.PollLocal();
		Assert.That(Account(tracker).CurrentGame!.StartedAt, Is.EqualTo(started), "same game keeps its start");

		_platform.RegistryRunningAppId = 220;
		tracker.PollLocal();
		Assert.That(Account(tracker).CurrentGame!.StartedAt, Is.EqualTo(_time.Now), "another game starts anew");
	}

	[Test]
	public void Closing_the_game_ends_the_session_and_the_next_one_starts_fresh()
	{
		_platform.RegistryRunningAppId = 730;
		var tracker = Tracker(false);
		tracker.PollLocal();

		_platform.RegistryRunningAppId = 0;
		tracker.PollLocal();
		Assert.That(Account(tracker).CurrentGame, Is.Null);

		_time.Now = _time.Now.AddHours(1);
		_platform.RegistryRunningAppId = 730;
		tracker.PollLocal();

		Assert.That(Account(tracker).CurrentGame!.StartedAt, Is.EqualTo(_time.Now));
	}

	[Test]
	public async Task The_web_api_game_of_an_account_is_used_when_nothing_runs_locally_but_not_for_this_computer()
	{
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, 440, "Team Fortress 2");
		var tracker = Tracker(true);
		tracker.PollLocal();

		await tracker.PollSelfAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Account(tracker).CurrentGame,
				Has.Property("AppId").EqualTo(440).And.Property("Name").EqualTo("Team Fortress 2"));
			Assert.That(tracker.Snapshot.CurrentGame, Is.Null, "this computer runs nothing");
		});
	}

	[Test]
	public async Task The_local_game_wins_over_the_web_api_game()
	{
		_platform.RegistryRunningAppId = 730;
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, 440, "Team Fortress 2");
		var tracker = Tracker(true);
		tracker.PollLocal();

		await tracker.PollSelfAsync(CancellationToken.None);

		Assert.That(Account(tracker).CurrentGame!.AppId, Is.EqualTo(730));
	}

	[Test]
	public async Task A_game_that_just_ended_locally_is_not_resurrected_by_a_web_status_that_lags_behind()
	{
		_platform.RegistryRunningAppId = 730;
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, 730, "Counter-Strike 2");
		var tracker = Tracker(true);
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);

		_platform.RegistryRunningAppId = 0;
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);
		Assert.That(Account(tracker).CurrentGame, Is.Null, "web still says playing, the local state is newer");

		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, null, null);
		await tracker.PollSelfAsync(CancellationToken.None);
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, 730, "Counter-Strike 2");
		await tracker.PollSelfAsync(CancellationToken.None);
		Assert.That(Account(tracker).CurrentGame?.AppId, Is.EqualTo(730), "a later session elsewhere counts again");
	}

	[Test]
	public async Task A_player_who_hides_game_details_is_not_playing_as_far_as_the_web_api_can_tell()
	{
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, null, null);
		var tracker = Tracker(true);
		tracker.PollLocal();

		await tracker.PollSelfAsync(CancellationToken.None);

		Assert.That(Account(tracker).CurrentGame, Is.Null);
	}

	[Test]
	public async Task Friends_are_counted_online_when_their_state_is_not_offline_and_in_game_when_they_have_a_game()
	{
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, null, null);
		_web.FriendIds = ["1", "2", "3", "4"];
		_web.Friends =
		[
			new SteamPlayerSummary("1", "a", 0, null, null),
			new SteamPlayerSummary("2", "b", 1, null, null),
			new SteamPlayerSummary("3", "c", 3, 730, "CS"),
			new SteamPlayerSummary("4", "d", 1, 570, "Dota")
		];
		var tracker = Tracker(true);
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);

		await tracker.PollFriendsAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Account(tracker).Friends, Is.EqualTo(SteamFriendsState.Available));
			Assert.That(Account(tracker).FriendsOnline, Is.EqualTo(3));
			Assert.That(Account(tracker).FriendsInGame, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task A_private_friend_list_is_distinguished_from_having_no_friends()
	{
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, null, null);
		_web.FriendIds = null;
		var tracker = Tracker(true);
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);

		await tracker.PollFriendsAsync(CancellationToken.None);
		Assert.That(Account(tracker).Friends, Is.EqualTo(SteamFriendsState.Private));

		_web.FriendIds = [];
		await tracker.PollFriendsAsync(CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(Account(tracker).Friends, Is.EqualTo(SteamFriendsState.Available));
			Assert.That(Account(tracker).FriendsOnline, Is.Zero);
		});
	}

	[Test]
	public async Task A_rejected_key_is_reported_and_replaces_a_private_friend_list()
	{
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, null, null);
		_web.FriendIds = null;
		var tracker = Tracker(true);
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);
		await tracker.PollFriendsAsync(CancellationToken.None);

		_web.SelfFailure = SteamWebFailure.InvalidKey;
		await tracker.PollSelfAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Account(tracker).Web, Is.EqualTo(SteamWebState.InvalidKey));
			Assert.That(Account(tracker).Friends, Is.EqualTo(SteamFriendsState.Unavailable));
		});
	}

	[Test]
	public async Task After_rate_limiting_the_web_api_is_left_alone_for_a_while()
	{
		_web.SelfFailure = SteamWebFailure.RateLimited;
		var tracker = Tracker(true);
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);
		var calls = _web.SelfCalls;

		await tracker.PollSelfAsync(CancellationToken.None);
		Assert.That(_web.SelfCalls, Is.EqualTo(calls), "still backing off");

		_time.Now = _time.Now.AddMinutes(3);
		await tracker.PollSelfAsync(CancellationToken.None);
		Assert.That(_web.SelfCalls, Is.EqualTo(calls + 1), "backoff over");
	}

	[Test]
	public async Task A_configuration_without_a_key_works_locally_and_never_calls_the_web_api()
	{
		_platform.RegistryRunningAppId = 220;
		var tracker = Tracker(false);

		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);
		await tracker.PollFriendsAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Account(tracker).Web, Is.EqualTo(SteamWebState.NotConfigured));
			Assert.That(Account(tracker).HasWebApi, Is.False);
			Assert.That(tracker.Snapshot.Games, Has.Count.EqualTo(2));
			Assert.That(Account(tracker).CurrentGame!.AppId, Is.EqualTo(220));
			Assert.That(_web.SelfCalls, Is.Zero);
		});
	}

	[Test]
	public void Every_change_is_published_and_an_unchanged_poll_is_not()
	{
		var tracker = Tracker(false);

		tracker.PollLocal();
		tracker.PollLocal();
		_platform.RegistryRunningAppId = 730;
		tracker.PollLocal();

		Assert.That(_published, Has.Count.EqualTo(2));
	}

	[Test]
	public void Variables_report_idle_values_while_nothing_is_played()
	{
		var tracker = Tracker(false);
		tracker.PollLocal();

		var now = _time.Now;
		Assert.Multiple(() =>
		{
			Assert.That(Read(SteamVariables.IsPlaying, tracker, now), Is.EqualTo(false));
			Assert.That(Read(SteamVariables.CurrentGameName, tracker, now), Is.EqualTo(string.Empty));
			Assert.That(Read(SteamVariables.CurrentGameId, tracker, now), Is.EqualTo(0));
			Assert.That(Read(SteamVariables.CurrentGamePlaytimeSeconds, tracker, now), Is.EqualTo(0L));
			Assert.That(Read(SteamVariables.GamesInstalled, tracker, now), Is.EqualTo(2));
		});
	}

	[Test]
	public void Variables_describe_the_running_game_and_its_session_time()
	{
		_platform.RegistryRunningAppId = 730;
		var tracker = Tracker(false);
		tracker.PollLocal();

		var later = _time.Now.AddSeconds(125);
		Assert.Multiple(() =>
		{
			Assert.That(Read(SteamVariables.IsPlaying, tracker, later), Is.EqualTo(true));
			Assert.That(Read(SteamVariables.CurrentGameName, tracker, later), Is.EqualTo("Counter-Strike 2"));
			Assert.That(Read(SteamVariables.CurrentGameId, tracker, later), Is.EqualTo(730));
			Assert.That(Read(SteamVariables.CurrentGamePlaytimeSeconds, tracker, later), Is.EqualTo(125L));
		});
	}

	[Test]
	public void Installed_games_and_friends_are_unavailable_when_they_cannot_be_known()
	{
		_platform.RegistrySteamPath = null;
		var tracker = Tracker(false);
		tracker.PollLocal();

		Assert.Multiple(() =>
		{
			Assert.That(Variable(SteamVariables.GamesInstalled, tracker), Is.EqualTo(VariableReading.Unavailable));
			Assert.That(Variable(SteamVariables.FriendsOnline, tracker), Is.EqualTo(VariableReading.Unavailable));
			Assert.That(Variable(SteamVariables.FriendsInGame, tracker), Is.EqualTo(VariableReading.Unavailable));
		});
	}

	[Test]
	public async Task Each_account_has_its_own_status_friends_and_game()
	{
		var other = new FakeWebApi
		{
			Self = new SteamPlayerSummary("76561198000000002", "Bob", 1, 440, "Team Fortress 2"),
			FriendIds = ["9"],
			Friends = [new SteamPlayerSummary("9", "z", 1, null, null)]
		};
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, null, null);
		_web.FriendIds = null;
		var tracker = new SteamTracker(_platform,
			[AccountConfig("e1", "Main", "main", _web), AccountConfig("e2", "Alt Account", "alt_account", other)],
			[],
			_time,
			_published.Add);
		tracker.PollLocal();

		await tracker.PollSelfAsync(CancellationToken.None);
		await tracker.PollFriendsAsync(CancellationToken.None);

		var first = tracker.Snapshot.Accounts[0];
		var second = tracker.Snapshot.Accounts[1];
		Assert.Multiple(() =>
		{
			Assert.That(first.Friends, Is.EqualTo(SteamFriendsState.Private));
			Assert.That(first.CurrentGame, Is.Null);
			Assert.That(second.Friends, Is.EqualTo(SteamFriendsState.Available));
			Assert.That(second.FriendsOnline, Is.EqualTo(1));
			Assert.That(second.CurrentGame, Has.Property("AppId").EqualTo(440));
		});
	}

	[Test]
	public async Task A_brief_web_outage_keeps_the_last_known_game_until_it_persists()
	{
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, 440, "Team Fortress 2");
		var tracker = Tracker(true);
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);
		var started = Account(tracker).CurrentGame!.StartedAt;

		_web.SelfFailure = SteamWebFailure.Unavailable;
		await tracker.PollSelfAsync(CancellationToken.None);
		await tracker.PollSelfAsync(CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(Account(tracker).Web, Is.EqualTo(SteamWebState.Connected));
			Assert.That(Account(tracker).CurrentGame?.StartedAt, Is.EqualTo(started), "no restart of the session");
		});

		await tracker.PollSelfAsync(CancellationToken.None);
		Assert.Multiple(() =>
		{
			Assert.That(Account(tracker).Web, Is.EqualTo(SteamWebState.Unavailable));
			Assert.That(Account(tracker).CurrentGame, Is.Null);
		});
	}

	[Test]
	public async Task A_failing_account_does_not_take_the_other_one_down()
	{
		var broken = new FakeWebApi { SelfFailure = SteamWebFailure.InvalidKey };
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, null, null);
		var tracker = new SteamTracker(_platform,
			[AccountConfig("e1", "Main", "main", _web), AccountConfig("e2", "Broken", "broken", broken)],
			[],
			_time,
			_published.Add);
		tracker.PollLocal();

		await tracker.PollSelfAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(tracker.Snapshot.Accounts[0].Web, Is.EqualTo(SteamWebState.Connected));
			Assert.That(tracker.Snapshot.Accounts[1].Web, Is.EqualTo(SteamWebState.InvalidKey));
		});
	}

	[Test]
	public async Task Variables_exist_per_account_only_and_carry_the_account_key()
	{
		_web.Self = new SteamPlayerSummary(SteamId, "Ann", 1, 440, "Team Fortress 2");
		_web.FriendIds = ["9", "8"];
		_web.Friends =
		[
			new SteamPlayerSummary("9", "a", 1, null, null),
			new SteamPlayerSummary("8", "b", 1, 730, "CS")
		];
		var tracker = Tracker(true);
		tracker.PollLocal();
		await tracker.PollSelfAsync(CancellationToken.None);
		await tracker.PollFriendsAsync(CancellationToken.None);
		var now = _time.Now;

		Assert.Multiple(() =>
		{
			Assert.That(Read(SteamVariables.IsPlaying, tracker, now), Is.EqualTo(true));
			Assert.That(Read(SteamVariables.CurrentGameName, tracker, now), Is.EqualTo("Team Fortress 2"));
			Assert.That(Read(SteamVariables.CurrentGameId, tracker, now), Is.EqualTo(440));
			Assert.That(Read(SteamVariables.FriendsOnline, tracker, now), Is.EqualTo(2));
			Assert.That(Read(SteamVariables.FriendsInGame, tracker, now), Is.EqualTo(1));
			Assert.That(SteamVariables.Read("steam_is_playing", tracker.Snapshot, now),
				Is.EqualTo(VariableReading.Unavailable),
				"there is no variable without an account name");
			Assert.That(SteamVariables.Read("steam-nobody-is-playing", tracker.Snapshot, now),
				Is.EqualTo(VariableReading.Unavailable));
		});
	}

	[Test]
	public void Every_account_declares_all_its_variables_under_its_own_prefix()
	{
		var definitions = SteamVariables.Declare("main", null);

		Assert.That(definitions.Select(definition => definition.Name),
			Is.EqualTo(new[]
			{
				"steam_main_is_playing",
				"steam_main_current_game_name",
				"steam_main_current_game_id",
				"steam_main_current_game_playtime_seconds",
				"steam_main_games_installed",
				"steam_main_friends_online",
				"steam_main_friends_ingame",
				"steam_main_games_owned",
				"steam_main_total_playtime_hours",
				"steam_main_recent_playtime_hours"
			}));
	}

	[TestCase("Main Account", "main_account")]
	[TestCase("Jörg's Büro", "joerg_s_buero")]
	[TestCase("  ", "account")]
	public void Account_keys_come_from_the_configuration_name_and_stay_unique(string title, string expected)
	{
		var used = new HashSet<string>(StringComparer.Ordinal);

		Assert.That(SteamVariables.UniqueKey(title, used), Is.EqualTo(expected));
		Assert.That(SteamVariables.UniqueKey(title, used), Is.EqualTo(expected + "_2"));
	}

	private SteamTracker Tracker(bool web)
	{
		return new SteamTracker(_platform,
			[web ? AccountConfig("e1", "Main", "main", _web) : new SteamAccountConfig("e1", "Main", "main")],
			[],
			_time,
			_published.Add);
	}

	private static SteamAccountConfig AccountConfig(string entryId, string title, string key, ISteamWebApiClient client)
	{
		return new SteamAccountConfig(entryId, title, key, SteamId, client);
	}

	private static SteamAccountSnapshot Account(SteamTracker tracker)
	{
		return tracker.Snapshot.Accounts[0];
	}

	private static object? Read(string name, SteamTracker tracker, DateTimeOffset now)
	{
		return SteamVariables.Read("steam_main_" + name, tracker.Snapshot, now).Value;
	}

	private static VariableReading Variable(string name, SteamTracker tracker)
	{
		return SteamVariables.Read("steam_main_" + name, tracker.Snapshot, DateTimeOffset.UnixEpoch);
	}

	private sealed class FakeWebApi : ISteamWebApiClient
	{
		public IReadOnlyList<SteamOwnedGame>? OwnedGames { get; set; }

		public SteamWebFailure? OwnedFailure { get; set; }

		public SteamPlayerSummary? Self { get; set; }

		public SteamWebFailure? SelfFailure { get; set; }

		public int SelfCalls { get; private set; }

		public IReadOnlyList<string>? FriendIds { get; set; } = [];

		public IReadOnlyList<SteamPlayerSummary> Friends { get; set; } = [];

		public Task<SteamPlayerSummary?> GetPlayerSummaryAsync(string steamId, CancellationToken cancellationToken)
		{
			SelfCalls++;
			return SelfFailure is { } failure
				? throw new SteamWebApiException(failure)
				: Task.FromResult(Self);
		}

		public Task<IReadOnlyList<SteamPlayerSummary>> GetPlayerSummariesAsync(
			IReadOnlyList<string> steamIds,
			CancellationToken cancellationToken)
		{
			return Task.FromResult(Friends);
		}

		public Task<IReadOnlyList<string>?> GetFriendIdsAsync(string steamId, CancellationToken cancellationToken)
		{
			return Task.FromResult(FriendIds);
		}

		public Task<string?> ResolveVanityUrlAsync(string vanityName, CancellationToken cancellationToken)
		{
			return Task.FromResult<string?>(null);
		}

		public Task<IReadOnlyList<SteamOwnedGame>?> GetOwnedGamesAsync(
			string steamId,
			CancellationToken cancellationToken)
		{
			return OwnedFailure is { } failure ? throw new SteamWebApiException(failure) : Task.FromResult(OwnedGames);
		}
	}
}
