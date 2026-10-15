using System.Globalization;
using MacroDeckHost.Integrations.Steam.Events;
using MacroDeckHost.Integrations.Steam.Native;
using MacroDeckHost.Integrations.Steam.Protocol;

namespace MacroDeckHost.Integrations.Steam.State;

internal sealed record SteamAccountConfig(
	string EntryId,
	string Title,
	string VariableKey,
	string? SteamId = null,
	ISteamWebApiClient? Client = null)
{
	public bool HasWebApi => SteamId is not null && Client is not null;
}

internal sealed class SteamTracker
{
	private const int FailuresBeforeUnavailable = 3;

	private static readonly TimeSpan _rateLimitBackoff = TimeSpan.FromMinutes(2);

	private static readonly TimeSpan _ownedGamesRefresh = TimeSpan.FromMinutes(10);

	private readonly ISteamPlatform _platform;
	private readonly SteamLibrary _library;
	private readonly IReadOnlyList<AccountState> _accounts;
	private readonly IReadOnlyList<string> _extraFolders;
	private readonly TimeProvider _time;
	private readonly Action<SteamSnapshot> _publish;
	private readonly Action<SteamEvent>? _events;
	private readonly Lock _sync = new();

	private string? _steamRoot;
	private SteamLocalState _local = SteamLocalState.None;
	private bool _localObserved;
	private int? _trackedAppId;
	private DateTimeOffset _trackedSince;
	private SteamSnapshot _snapshot = SteamSnapshot.Empty;

	public SteamTracker(
		ISteamPlatform platform,
		IReadOnlyList<SteamAccountConfig> accounts,
		IReadOnlyList<string> extraFolders,
		TimeProvider time,
		Action<SteamSnapshot> publish,
		Action<SteamEvent>? events = null)
	{
		_platform = platform;
		_library = new SteamLibrary(time);
		_accounts = [.. accounts.Select(account => new AccountState(account))];
		_extraFolders = extraFolders;
		_time = time;
		_publish = publish;
		_events = events;
	}

	public SteamSnapshot Snapshot
	{
		get
		{
			lock (_sync)
			{
				return _snapshot;
			}
		}
	}

	public bool SteamFound => Snapshot.SteamFound;

	public bool HasWebApi => _accounts.Any(account => account.Config.HasWebApi);

	public void PollLocal()
	{
		var root = _steamRoot is { } known && SteamInstallLocator.IsSteamRoot(known)
			? known
			: SteamInstallLocator.Find(_platform);

		SteamLocalState local;
		if (root is null)
		{
			local = SteamLocalState.None;
		}
		else
		{
			local = new SteamLocalState(true,
				_library.Scan(root, _extraFolders),
				SteamRunningApp.Read(_platform, root));
		}

		Commit(() =>
			{
				_steamRoot = root;
				_local = local;
			},
			true);
	}

	public async Task PollSelfAsync(CancellationToken cancellationToken)
	{
		foreach (var account in _accounts.Where(account => account.Config.HasWebApi))
		{
			if (IsBackingOff(account))
			{
				continue;
			}

			SteamPlayerSummary? summary = null;
			SteamWebState state;
			try
			{
				summary = await account.Config.Client!.GetPlayerSummaryAsync(account.Config.SteamId!,
					cancellationToken);
				state = SteamWebState.Connected;
			}
			catch (SteamWebApiException exception)
			{
				state = FailureState(account, exception.Failure);
			}

			Commit(() =>
			{
				if (state == SteamWebState.Unavailable)
				{
					account.Failures++;
					if (account.Web == SteamWebState.Connected && account.Failures < FailuresBeforeUnavailable)
					{
						return;
					}
				}
				else
				{
					account.Failures = 0;
				}

				account.Self = state == SteamWebState.Connected ? summary : null;
				account.Web = state;
				if (state == SteamWebState.InvalidKey)
				{
					account.Friends = SteamFriendsState.Unavailable;
					account.Owned = SteamOwnedState.Unavailable;
				}

				if (account.Self?.GameId != account.StaleWebAppId)
				{
					account.StaleWebAppId = null;
				}
			});
		}
	}

	public async Task PollFriendsAsync(CancellationToken cancellationToken)
	{
		foreach (var account in _accounts.Where(account => account.Config.HasWebApi))
		{
			if (IsBackingOff(account) || !IsReachable(account))
			{
				continue;
			}

			var friendsState = SteamFriendsState.Unavailable;
			IReadOnlyList<SteamFriend> friends = [];
			var failure = default(SteamWebFailure?);

			try
			{
				if (await account.Config.Client!.GetFriendIdsAsync(account.Config.SteamId!, cancellationToken) is not
					{ } ids)
				{
					friendsState = SteamFriendsState.Private;
				}
				else
				{
					var summaries = await account.Config.Client.GetPlayerSummariesAsync(ids, cancellationToken);
					friends = Sort(summaries.Select(ToFriend));
					friendsState = SteamFriendsState.Available;
				}
			}
			catch (SteamWebApiException exception)
			{
				failure = exception.Failure;
			}

			Commit(() =>
			{
				if (failure is { } reason)
				{
					if (FailureState(account, reason) == SteamWebState.InvalidKey)
					{
						account.Web = SteamWebState.InvalidKey;
					}

					account.Friends = SteamFriendsState.Unavailable;
					account.FriendList = [];
				}
				else
				{
					account.Friends = friendsState;
					account.FriendList = friends;
				}
			});
		}
	}

	public async Task PollOwnedGamesAsync(CancellationToken cancellationToken)
	{
		foreach (var account in _accounts.Where(account => account.Config.HasWebApi))
		{
			if (IsBackingOff(account) || !IsReachable(account) || !IsOwnedGamesDue(account))
			{
				continue;
			}

			IReadOnlyList<SteamOwnedGame>? games = null;
			var failure = default(SteamWebFailure?);

			try
			{
				games = await account.Config.Client!.GetOwnedGamesAsync(account.Config.SteamId!, cancellationToken);
			}
			catch (SteamWebApiException exception)
			{
				failure = exception.Failure;
			}

			Commit(() =>
			{
				if (failure is { } reason)
				{
					if (FailureState(account, reason) == SteamWebState.InvalidKey)
					{
						account.Web = SteamWebState.InvalidKey;
						account.Owned = SteamOwnedState.Unavailable;
						account.OwnedGames = [];
					}

					return;
				}

				account.OwnedFetchedAt = _time.GetUtcNow();
				account.Owned = games is null ? SteamOwnedState.Private : SteamOwnedState.Available;
				account.OwnedGames = games is null
					? []
					:
					[
						.. games.OrderByDescending(game => game.LastPlayed ?? DateTimeOffset.MinValue)
							.ThenByDescending(game => game.PlaytimeMinutes)
							.ThenBy(game => game.AppId)
					];
			});
		}
	}

	private static SteamFriend ToFriend(SteamPlayerSummary summary)
	{
		return new SteamFriend(summary.SteamId,
			string.IsNullOrWhiteSpace(summary.Name) ? summary.SteamId : summary.Name,
			summary.PersonaState,
			summary.GameId,
			summary.GameId is not null && !string.IsNullOrWhiteSpace(summary.GameName) ? summary.GameName : null,
			summary.AvatarUrl);
	}

	private static IReadOnlyList<SteamFriend> Sort(IEnumerable<SteamFriend> friends)
	{
		return
		[
			.. friends.OrderBy(friend => friend.IsInGame ? 0 : friend.IsOnline ? 1 : 2)
				.ThenBy(friend => friend.PersonaState is SteamPersona.Online
					or SteamPersona.LookingToPlay
					or SteamPersona.LookingToTrade
					? 0
					: 1)
				.ThenBy(friend => friend.Name, StringComparer.CurrentCultureIgnoreCase)
				.ThenBy(friend => friend.SteamId, StringComparer.Ordinal)
		];
	}

	private bool IsOwnedGamesDue(AccountState account)
	{
		lock (_sync)
		{
			return account.Owned == SteamOwnedState.Unavailable
				|| _time.GetUtcNow() - account.OwnedFetchedAt >= _ownedGamesRefresh;
		}
	}

	private bool IsReachable(AccountState account)
	{
		lock (_sync)
		{
			return account.Web is SteamWebState.Connected or SteamWebState.Connecting;
		}
	}

	private void Commit(Action mutate, bool localPoll = false)
	{
		SteamSnapshot? changed;
		lock (_sync)
		{
			var previous = _snapshot;
			var localKnown = _localObserved;
			mutate();
			_localObserved |= localPoll;
			changed = Rebuild();

			// Events leave in commit order: the polls commit from several threads, and a diff taken outside the
			// lock could run backwards. The sink only queues, so holding the lock while it runs is safe.
			if (_events is not null)
			{
				foreach (var item in Diff(previous, _snapshot, localKnown))
				{
					_events(item);
				}
			}
		}

		if (changed is not null)
		{
			_publish(changed);
		}
	}

	private List<SteamEvent> Diff(SteamSnapshot previous, SteamSnapshot current, bool localKnown)
	{
		var events = new List<SteamEvent>();
		var now = _time.GetUtcNow();

		if (localKnown && previous.CurrentGame?.AppId != current.CurrentGame?.AppId)
		{
			if (previous.CurrentGame is { } stopped)
			{
				events.Add(SteamEventFactory.GameStopped(SteamEventPayload.ThisComputer,
					stopped.AppId,
					stopped.Name,
					now - stopped.StartedAt));
			}

			if (current.CurrentGame is { } started)
			{
				events.Add(SteamEventFactory.GameStarted(SteamEventPayload.ThisComputer, started.AppId, started.Name));
			}
		}

		foreach (var account in _accounts)
		{
			var entryId = account.Config.EntryId;
			var before = previous.Accounts.FirstOrDefault(candidate => candidate.EntryId == entryId);
			var after = current.Accounts.FirstOrDefault(candidate => candidate.EntryId == entryId);

			DiffRemoteGame(account, current.CurrentGame?.AppId, now, events);

			if (before is null || after is null)
			{
				continue;
			}

			if (before.Persona is { } oldPersona && after.Persona is { } newPersona && oldPersona != newPersona)
			{
				events.Add(SteamEventFactory.PersonaChanged(entryId, oldPersona, newPersona));
			}

			if (before.Friends == SteamFriendsState.Available && after.Friends == SteamFriendsState.Available)
			{
				events.AddRange(SteamEventFactory.FriendChanges(entryId, before.FriendList, after.FriendList));
			}
		}

		return events;
	}

	private void DiffRemoteGame(AccountState account, int? localAppId, DateTimeOffset now, List<SteamEvent> events)
	{
		if (account.Web != SteamWebState.Connected)
		{
			account.RemoteObserved = false;
			return;
		}

		var webAppId = account.Self?.GameId;
		if (webAppId is not null && (webAppId == localAppId || webAppId == account.StaleWebAppId))
		{
			return;
		}

		if (!account.RemoteObserved)
		{
			account.RemoteObserved = true;
			account.RemoteGame = webAppId is { } first
				? new SteamCurrentGame(first, NameOf(first, account.Self), now)
				: null;
			return;
		}

		if (account.RemoteGame?.AppId == webAppId)
		{
			return;
		}

		if (account.RemoteGame is { } stopped)
		{
			events.Add(SteamEventFactory.GameStopped(account.Config.EntryId,
				stopped.AppId,
				stopped.Name,
				now - stopped.StartedAt));
		}

		account.RemoteGame
			= webAppId is { } appId ? new SteamCurrentGame(appId, NameOf(appId, account.Self), now) : null;
		if (account.RemoteGame is { } started)
		{
			events.Add(SteamEventFactory.GameStarted(account.Config.EntryId, started.AppId, started.Name));
		}
	}

	private bool IsBackingOff(AccountState account)
	{
		lock (_sync)
		{
			return _time.GetUtcNow() < account.BackoffUntil;
		}
	}

	private SteamWebState FailureState(AccountState account, SteamWebFailure failure)
	{
		if (failure == SteamWebFailure.RateLimited)
		{
			lock (_sync)
			{
				account.BackoffUntil = _time.GetUtcNow() + _rateLimitBackoff;
			}
		}

		return failure == SteamWebFailure.InvalidKey ? SteamWebState.InvalidKey : SteamWebState.Unavailable;
	}

	private SteamSnapshot? Rebuild()
	{
		var local = ResolveLocalGame();
		var snapshot = new SteamSnapshot
		{
			SteamFound = _local.Found,
			Games = _local.Games,
			CurrentGame = local,
			Accounts = [.. _accounts.Select(account => ToSnapshot(account, local))]
		};

		if (snapshot == _snapshot)
		{
			return null;
		}

		_snapshot = snapshot;
		return snapshot;
	}

	private SteamAccountSnapshot ToSnapshot(AccountState account, SteamCurrentGame? local)
	{
		if (account.LastLocalAppId is { } previous && local is null)
		{
			account.StaleWebAppId = previous;
		}
		else if (local is not null)
		{
			account.StaleWebAppId = null;
		}

		account.LastLocalAppId = local?.AppId;

		var game = local;
		if (game is null
			&& account.Web == SteamWebState.Connected
			&& account.Self?.GameId is { } webAppId
			&& webAppId != account.StaleWebAppId)
		{
			if (account.TrackedAppId != webAppId)
			{
				account.TrackedAppId = webAppId;
				account.TrackedSince = _time.GetUtcNow();
			}

			game = new SteamCurrentGame(webAppId, NameOf(webAppId, account.Self), account.TrackedSince);
		}
		else
		{
			account.TrackedAppId = null;
		}

		var connected = account.Web == SteamWebState.Connected;
		var friends = connected ? account.Friends : SteamFriendsState.Unavailable;
		var friendList = friends == SteamFriendsState.Available ? account.FriendList : [];
		var owned = connected ? account.Owned : SteamOwnedState.Unavailable;

		return new SteamAccountSnapshot(account.Config.EntryId, account.Config.Title, account.Config.VariableKey)
		{
			Web = account.Web,
			Friends = friends,
			FriendsOnline = friendList.Count(friend => friend.IsOnline),
			FriendsInGame = friendList.Count(friend => friend.IsInGame),
			FriendList = friendList,
			Persona = connected ? account.Self?.PersonaState : null,
			Owned = owned,
			OwnedGames = owned == SteamOwnedState.Available ? account.OwnedGames : [],
			CurrentGame = game
		};
	}

	private string NameOf(int appId, SteamPlayerSummary? self)
	{
		return _local.Games.FirstOrDefault(game => game.AppId == appId)?.Name
			?? (self?.GameId == appId && !string.IsNullOrWhiteSpace(self.GameName) ? self.GameName : null)
			?? appId.ToString(CultureInfo.InvariantCulture);
	}

	private SteamCurrentGame? ResolveLocalGame()
	{
		if (_local.RunningAppId is not { } appId)
		{
			_trackedAppId = null;
			return null;
		}

		if (_trackedAppId != appId)
		{
			_trackedAppId = appId;
			_trackedSince = _time.GetUtcNow();
		}

		return new SteamCurrentGame(appId, NameOf(appId, null), _trackedSince);
	}

	private sealed class AccountState
	{
		public AccountState(SteamAccountConfig config)
		{
			Config = config;
			Web = config.HasWebApi ? SteamWebState.Connecting : SteamWebState.NotConfigured;
		}

		public SteamAccountConfig Config { get; }

		public SteamPlayerSummary? Self { get; set; }

		public SteamWebState Web { get; set; }

		public SteamFriendsState Friends { get; set; }

		public IReadOnlyList<SteamFriend> FriendList { get; set; } = [];

		public SteamOwnedState Owned { get; set; }

		public IReadOnlyList<SteamOwnedGame> OwnedGames { get; set; } = [];

		public DateTimeOffset OwnedFetchedAt { get; set; }

		public DateTimeOffset BackoffUntil { get; set; }

		public int Failures { get; set; }

		public int? TrackedAppId { get; set; }

		public DateTimeOffset TrackedSince { get; set; }

		public int? LastLocalAppId { get; set; }

		public int? StaleWebAppId { get; set; }

		public bool RemoteObserved { get; set; }

		public SteamCurrentGame? RemoteGame { get; set; }
	}

	private sealed record SteamLocalState(bool Found, IReadOnlyList<SteamGame> Games, int? RunningAppId)
	{
		public static readonly SteamLocalState None = new(false, [], null);
	}
}
