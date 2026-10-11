using System.Text;
using MacroDeck.Localization;
using MacroDeck.Sdk.Variables;
using Strings = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Variables;

namespace MacroDeckHost.Integrations.Steam;

internal static class SteamVariables
{
	public const string Prefix = "steam_";

	public const string IsPlaying = "is_playing";

	public const string CurrentGameName = "current_game_name";

	public const string CurrentGameId = "current_game_id";

	public const string CurrentGamePlaytimeSeconds = "current_game_playtime_seconds";

	public const string GamesInstalled = "games_installed";

	public const string FriendsOnline = "friends_online";

	public const string FriendsInGame = "friends_ingame";

	public const string GamesOwned = "games_owned";

	public const string TotalPlaytimeHours = "total_playtime_hours";

	public const string RecentPlaytimeHours = "recent_playtime_hours";

	private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(3);

	public static IReadOnlyList<string> Names { get; } =
		[
			IsPlaying, CurrentGameName, CurrentGameId, CurrentGamePlaytimeSeconds, GamesInstalled, FriendsOnline, FriendsInGame,
			GamesOwned, TotalPlaytimeHours, RecentPlaytimeHours
		];

	public static string NameOf(string variableKey, string name) => $"{Prefix}{variableKey}_{name}";

	public static IReadOnlyList<VariableDefinition> Declare(string variableKey, VariableConfiguration? configuration)
		=>
		[
			Define(variableKey, IsPlaying, VariableType.Boolean, Strings.IsPlaying(), configuration),
			Define(variableKey, CurrentGameName, VariableType.Text, Strings.CurrentGameName(), configuration),
			Define(variableKey, CurrentGameId, VariableType.Numeric, Strings.CurrentGameId(), configuration,
				decimalPlaces: 0),
			Define(variableKey, CurrentGamePlaytimeSeconds, VariableType.Numeric, Strings.CurrentGamePlaytimeSeconds(),
				configuration, decimalPlaces: 0, unit: "s"),
			Define(variableKey, GamesInstalled, VariableType.Numeric, Strings.GamesInstalled(), configuration,
				decimalPlaces: 0),
			Define(variableKey, FriendsOnline, VariableType.Numeric, Strings.FriendsOnline(), configuration,
				decimalPlaces: 0),
			Define(variableKey, FriendsInGame, VariableType.Numeric, Strings.FriendsInGame(), configuration,
				decimalPlaces: 0),
			Define(variableKey, GamesOwned, VariableType.Numeric, Strings.GamesOwned(), configuration, decimalPlaces: 0),
			Define(variableKey, TotalPlaytimeHours, VariableType.Numeric, Strings.TotalPlaytimeHours(), configuration,
				decimalPlaces: 1, unit: "h"),
			Define(variableKey, RecentPlaytimeHours, VariableType.Numeric, Strings.RecentPlaytimeHours(), configuration,
				decimalPlaces: 1, unit: "h")
		];

	public static VariableReading Read(string localId, SteamSnapshot snapshot, DateTimeOffset now)
	{
		var id = localId.Replace('-', '_');
		if (!id.StartsWith(Prefix, StringComparison.Ordinal))
		{
			return VariableReading.Unavailable;
		}

		var remainder = id[Prefix.Length..];
		foreach (var account in snapshot.Accounts)
		{
			foreach (var name in Names)
			{
				if (remainder == $"{account.VariableKey}_{name}")
				{
					return Read(name, account, snapshot, now);
				}
			}
		}

		return VariableReading.Unavailable;
	}

	public static string UniqueKey(string title, HashSet<string> used)
	{
		var baseKey = Slugify(title);
		if (baseKey.Length == 0)
		{
			baseKey = "account";
		}

		var key = baseKey;
		var suffix = 2;
		while (!used.Add(key))
		{
			key = $"{baseKey}_{suffix++}";
		}

		return key;
	}

	public static string Slugify(string title)
	{
		var builder = new StringBuilder(title.Length);
		foreach (var ch in title.ToLowerInvariant())
		{
			switch (ch)
			{
				case 'ä':
					builder.Append("ae");
					break;
				case 'ö':
					builder.Append("oe");
					break;
				case 'ü':
					builder.Append("ue");
					break;
				case 'ß':
					builder.Append("ss");
					break;
				case >= 'a' and <= 'z':
				case >= '0' and <= '9':
					builder.Append(ch);
					break;
				default:
					if (builder.Length > 0 && builder[^1] != '_')
					{
						builder.Append('_');
					}

					break;
			}
		}

		return builder.ToString().Trim('_');
	}

	private static VariableReading Read(string name, SteamAccountSnapshot account, SteamSnapshot snapshot, DateTimeOffset now)
	{
		var game = account.CurrentGame;

		return name switch
		{
			IsPlaying => VariableReading.Of(game is not null),
			CurrentGameName => VariableReading.Of(game?.Name ?? string.Empty),
			CurrentGameId => VariableReading.Of(game?.AppId ?? 0),
			CurrentGamePlaytimeSeconds => VariableReading.Of(game is null
				? 0L
				: Math.Max(0L, (long)(now - game.StartedAt).TotalSeconds)),
			GamesInstalled => snapshot.SteamFound ? VariableReading.Of(snapshot.Games.Count) : VariableReading.Unavailable,
			FriendsOnline => Friends(account, candidate => candidate.FriendsOnline),
			FriendsInGame => Friends(account, candidate => candidate.FriendsInGame),
			GamesOwned => Owned(account, games => games.Count),
			TotalPlaytimeHours => Owned(account, games => Hours(games.Sum(game => (long)game.PlaytimeMinutes))),
			RecentPlaytimeHours => Owned(account, games => Hours(games.Sum(game => (long)game.RecentMinutes))),
			_ => VariableReading.Unavailable
		};
	}

	private static VariableReading Friends(SteamAccountSnapshot account, Func<SteamAccountSnapshot, int> select)
		=> account is { Web: SteamWebState.Connected, Friends: SteamFriendsState.Available }
			? VariableReading.Of(select(account))
			: VariableReading.Unavailable;

	private static VariableReading Owned(SteamAccountSnapshot account, Func<IReadOnlyList<SteamOwnedGame>, double> select)
		=> account is { Web: SteamWebState.Connected, Owned: SteamOwnedState.Available }
			? VariableReading.Of(select(account.OwnedGames))
			: VariableReading.Unavailable;

	internal static double Hours(long minutes) => Math.Round(minutes / 60d, 1, MidpointRounding.AwayFromZero);

	private static VariableDefinition Define(
		string variableKey,
		string name,
		VariableType type,
		LocalizedText displayName,
		VariableConfiguration? configuration,
		int? decimalPlaces = null,
		string? unit = null)
		=> VariableDefinition.Eager(NameOf(variableKey, name), type, decimalPlaces, _refreshInterval)
			with
			{
				DisplayName = displayName,
				Configuration = configuration,
				Unit = unit
			};
}
