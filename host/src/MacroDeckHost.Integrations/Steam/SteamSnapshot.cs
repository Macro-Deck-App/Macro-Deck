namespace MacroDeckHost.Integrations.Steam;

public sealed record SteamGame(int AppId, string Name, DateTimeOffset? LastPlayed);

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

public sealed record SteamCurrentGame(int AppId, string Name, DateTimeOffset StartedAt);

public sealed record SteamAccountSnapshot(string EntryId, string Title, string VariableKey)
{
	public SteamWebState Web { get; init; } = SteamWebState.NotConfigured;

	public SteamFriendsState Friends { get; init; } = SteamFriendsState.Unavailable;

	public int FriendsOnline { get; init; }

	public int FriendsInGame { get; init; }

	public SteamCurrentGame? CurrentGame { get; init; }

	public bool HasWebApi => Web != SteamWebState.NotConfigured;
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
		=> other is not null &&
			SteamFound == other.SteamFound &&
			Equals(CurrentGame, other.CurrentGame) &&
			Games.SequenceEqual(other.Games) &&
			Accounts.SequenceEqual(other.Accounts);

	public override int GetHashCode() => HashCode.Combine(SteamFound, CurrentGame, Games.Count, Accounts.Count);
}

public enum SteamCommandKind
{
	LaunchGame,

	LaunchSteam,

	OpenStorePage,

	OpenLibraryPage
}

public sealed record SteamCommand(SteamCommandKind Kind, int AppId = 0);

public enum SteamCommandOutcome
{
	Succeeded,

	NotAvailable,

	InvalidAppId,

	Failed
}
