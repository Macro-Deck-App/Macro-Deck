using System.Globalization;
using MacroDeckHost.Application.Steam;

namespace MacroDeckHost.Integrations.Steam;

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

	private readonly ISteamPlatform _platform;
	private readonly SteamLibrary _library;
	private readonly IReadOnlyList<AccountState> _accounts;
	private readonly IReadOnlyList<string> _extraFolders;
	private readonly TimeProvider _time;
	private readonly Action<SteamSnapshot> _publish;
	private readonly Lock _sync = new();

	private string? _steamRoot;
	private SteamLocalState _local = SteamLocalState.None;
	private int? _trackedAppId;
	private DateTimeOffset _trackedSince;
	private SteamSnapshot _snapshot = SteamSnapshot.Empty;

	public SteamTracker(
		ISteamPlatform platform,
		IReadOnlyList<SteamAccountConfig> accounts,
		IReadOnlyList<string> extraFolders,
		TimeProvider time,
		Action<SteamSnapshot> publish)
	{
		_platform = platform;
		_library = new SteamLibrary(time);
		_accounts = [.. accounts.Select(account => new AccountState(account))];
		_extraFolders = extraFolders;
		_time = time;
		_publish = publish;
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
		});
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
				summary = await account.Config.Client!.GetPlayerSummaryAsync(account.Config.SteamId!, cancellationToken);
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
			if (IsBackingOff(account))
			{
				continue;
			}

			lock (_sync)
			{
				if (account.Web is not (SteamWebState.Connected or SteamWebState.Connecting))
				{
					continue;
				}
			}

			var friendsState = SteamFriendsState.Unavailable;
			var online = 0;
			var inGame = 0;
			var failure = default(SteamWebFailure?);

			try
			{
				if (await account.Config.Client!.GetFriendIdsAsync(account.Config.SteamId!, cancellationToken) is not { } ids)
				{
					friendsState = SteamFriendsState.Private;
				}
				else
				{
					var friends = await account.Config.Client.GetPlayerSummariesAsync(ids, cancellationToken);
					online = friends.Count(friend => friend.PersonaState != 0);
					inGame = friends.Count(friend => friend.GameId is not null);
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
				}
				else
				{
					account.Friends = friendsState;
					account.Online = online;
					account.InGame = inGame;
				}
			});
		}
	}

	private void Commit(Action mutate)
	{
		SteamSnapshot? changed;
		lock (_sync)
		{
			mutate();
			changed = Rebuild();
		}

		if (changed is not null)
		{
			_publish(changed);
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
		if (game is null &&
			account.Web == SteamWebState.Connected &&
			account.Self?.GameId is { } webAppId &&
			webAppId != account.StaleWebAppId)
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

		return new SteamAccountSnapshot(account.Config.EntryId, account.Config.Title, account.Config.VariableKey)
		{
			Web = account.Web,
			Friends = account.Web == SteamWebState.Connected ? account.Friends : SteamFriendsState.Unavailable,
			FriendsOnline = account.Online,
			FriendsInGame = account.InGame,
			CurrentGame = game
		};
	}

	private string NameOf(int appId, SteamPlayerSummary? self)
		=> _local.Games.FirstOrDefault(game => game.AppId == appId)?.Name ??
			(self?.GameId == appId && !string.IsNullOrWhiteSpace(self.GameName) ? self.GameName : null) ??
			appId.ToString(CultureInfo.InvariantCulture);

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

		public int Online { get; set; }

		public int InGame { get; set; }

		public DateTimeOffset BackoffUntil { get; set; }

		public int Failures { get; set; }

		public int? TrackedAppId { get; set; }

		public DateTimeOffset TrackedSince { get; set; }

		public int? LastLocalAppId { get; set; }

		public int? StaleWebAppId { get; set; }
	}

	private sealed record SteamLocalState(bool Found, IReadOnlyList<SteamGame> Games, int? RunningAppId)
	{
		public static readonly SteamLocalState None = new(false, [], null);
	}
}
