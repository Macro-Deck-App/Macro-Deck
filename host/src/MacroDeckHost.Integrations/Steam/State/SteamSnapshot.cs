namespace MacroDeckHost.Integrations.Steam.State;

public sealed record SteamGame(int AppId, string Name, DateTimeOffset? LastPlayed, string? InstallDirectory = null);

public enum SteamWebState
{
	NotConfigured,

	Connecting,

	Connected,

	InvalidKey,

	Unavailable
}

public enum SteamFriendsState
{
	Unavailable,

	Private,

	Available
}

public enum SteamOwnedState
{
	Unavailable,

	Private,

	Available
}

public sealed record SteamCurrentGame(int AppId, string Name, DateTimeOffset StartedAt);

public sealed record SteamFriend(
	string SteamId,
	string Name,
	int PersonaState,
	int? GameId,
	string? GameName,
	string? AvatarUrl)
{
	public bool IsOnline => PersonaState != SteamPersona.Offline;

	public bool IsInGame => GameId is not null;
}

public sealed record SteamOwnedGame(
	int AppId,
	string Name,
	int PlaytimeMinutes,
	int RecentMinutes,
	DateTimeOffset? LastPlayed);

public sealed record SteamAccountSnapshot(string EntryId, string Title, string VariableKey)
{
	public SteamWebState Web { get; init; } = SteamWebState.NotConfigured;

	public SteamFriendsState Friends { get; init; } = SteamFriendsState.Unavailable;

	public int FriendsOnline { get; init; }

	public int FriendsInGame { get; init; }

	public IReadOnlyList<SteamFriend> FriendList { get; init; } = [];

	public int? Persona { get; init; }

	public SteamOwnedState Owned { get; init; } = SteamOwnedState.Unavailable;

	public IReadOnlyList<SteamOwnedGame> OwnedGames { get; init; } = [];

	public SteamCurrentGame? CurrentGame { get; init; }

	public bool HasWebApi => Web != SteamWebState.NotConfigured;

	public bool Equals(SteamAccountSnapshot? other)
	{
		return other is not null
			&& EntryId == other.EntryId
			&& Title == other.Title
			&& VariableKey == other.VariableKey
			&& Web == other.Web
			&& Friends == other.Friends
			&& FriendsOnline == other.FriendsOnline
			&& FriendsInGame == other.FriendsInGame
			&& Persona == other.Persona
			&& Owned == other.Owned
			&& Equals(CurrentGame, other.CurrentGame)
			&& FriendList.SequenceEqual(other.FriendList)
			&& OwnedGames.SequenceEqual(other.OwnedGames);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(EntryId, Web, Friends, Persona, Owned, CurrentGame);
	}
}

public sealed record SteamSnapshot
{
	public static readonly SteamSnapshot Empty = new();

	public bool SteamFound { get; init; }

	public IReadOnlyList<SteamGame> Games { get; init; } = [];

	public SteamCurrentGame? CurrentGame { get; init; }

	public IReadOnlyList<SteamAccountSnapshot> Accounts { get; init; } = [];


	public bool IsPlaying => CurrentGame is not null;

	public bool Equals(SteamSnapshot? other)
	{
		return other is not null
			&& SteamFound == other.SteamFound
			&& Equals(CurrentGame, other.CurrentGame)
			&& Games.SequenceEqual(other.Games)
			&& Accounts.SequenceEqual(other.Accounts);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(SteamFound, CurrentGame, Games.Count, Accounts.Count);
	}
}

public static class SteamPersona
{
	public const int Offline = 0;

	public const int Online = 1;

	public const int Busy = 2;

	public const int Away = 3;

	public const int Snooze = 4;

	public const int LookingToTrade = 5;

	public const int LookingToPlay = 6;

	public static string Id(int state)
	{
		return state switch
		{
			Offline => "offline",
			Busy => "busy",
			Away => "away",
			Snooze => "snooze",
			LookingToTrade => "trade",
			LookingToPlay => "play",
			_ => "online"
		};
	}
}

public enum SteamCommandKind
{
	LaunchGame,

	LaunchSteam,

	OpenStorePage,

	OpenLibraryPage,

	CloseGame
}

public sealed record SteamCommand(SteamCommandKind Kind, int AppId = 0, bool Force = false);

public enum SteamCommandOutcome
{
	Succeeded,

	NotAvailable,

	InvalidAppId,

	Failed,

	NotRunning
}
