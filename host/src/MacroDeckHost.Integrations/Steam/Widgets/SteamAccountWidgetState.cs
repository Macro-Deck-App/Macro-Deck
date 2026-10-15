using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Integrations.Steam.State;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.Steam;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal sealed record SteamFriendRow(
	string Key,
	string Name,
	LocalizedText Status,
	string StatusColor,
	UiResource? Avatar,
	string Initial);

internal sealed record SteamFriendsWidgetState(
	SteamWidgetStatus Status,
	int Online,
	int InGame,
	IReadOnlyList<SteamFriendRow> Rows,
	UiResource? Glyph)
{
	public static SteamFriendsWidgetState Empty { get; } = new(SteamWidgetStatus.Unavailable, 0, 0, [], null);

	public IReadOnlyList<SteamFriendRow> Take(int count)
	{
		return [.. Rows.Take(count)];
	}

	public bool Equals(SteamFriendsWidgetState? other)
	{
		return other is not null
			&& Status == other.Status
			&& Online == other.Online
			&& InGame == other.InGame
			&& Equals(Glyph, other.Glyph)
			&& Rows.SequenceEqual(other.Rows);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Status, Online, InGame, Rows.Count);
	}
}

internal sealed record SteamLibraryGame(string Key, int AppId, string Name, UiResource? Cover, LocalizedText Detail);

internal sealed record SteamLibraryWidgetState(
	SteamWidgetStatus Status,
	bool FromAccount,
	int GameCount,
	string TotalHours,
	string RecentHours,
	IReadOnlyList<SteamLibraryGame> Recent,
	UiResource? Glyph)
{
	public static SteamLibraryWidgetState Empty { get; } =
		new(SteamWidgetStatus.Unavailable, false, 0, string.Empty, string.Empty, [], null);

	public IReadOnlyList<SteamLibraryGame> Take(int count)
	{
		return [.. Recent.Take(count)];
	}

	public bool Equals(SteamLibraryWidgetState? other)
	{
		return other is not null
			&& Status == other.Status
			&& FromAccount == other.FromAccount
			&& GameCount == other.GameCount
			&& TotalHours == other.TotalHours
			&& RecentHours == other.RecentHours
			&& Equals(Glyph, other.Glyph)
			&& Recent.SequenceEqual(other.Recent);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Status, FromAccount, GameCount, TotalHours, Recent.Count);
	}
}

internal static class SteamAccountWidgetStates
{
	public const int MaxFriendRows = 6;

	public const int MaxRecentGames = 4;

	public static SteamFriendsWidgetState Friends(
		ISteamSource? source,
		SteamAccountOptions options,
		SteamArtworkCache artwork,
		UiResource? glyph)
	{
		if (source is not { IsAvailable: true })
		{
			return SteamFriendsWidgetState.Empty with { Glyph = glyph };
		}

		var (status, account) = Resolve(source.Snapshot, options);
		if (status is not null)
		{
			return SteamFriendsWidgetState.Empty with { Status = status.Value, Glyph = glyph };
		}

		if (account!.Friends != SteamFriendsState.Available)
		{
			return SteamFriendsWidgetState.Empty with
			{
				Status = account.Friends == SteamFriendsState.Private
					? SteamWidgetStatus.Private
					: SteamWidgetStatus.AccountUnavailable,
				Glyph = glyph
			};
		}

		var rows = account.FriendList.Where(friend => friend.IsOnline)
			.Take(MaxFriendRows)
			.Select(friend =>
			{
				artwork.TrackAvatar(friend.AvatarUrl);
				return new SteamFriendRow($"friend-{friend.SteamId}",
					friend.Name,
					Status(friend),
					SteamStyle.PersonaColor(friend),
					artwork.FindAvatar(friend.AvatarUrl),
					Initial(friend.Name));
			})
			.ToList();

		return new SteamFriendsWidgetState(SteamWidgetStatus.Ready,
			account.FriendsOnline,
			account.FriendsInGame,
			rows,
			glyph);
	}

	public static SteamLibraryWidgetState Library(
		ISteamSource? source,
		SteamAccountOptions options,
		SteamArtworkCache artwork,
		UiResource? glyph)
	{
		if (source is not { IsAvailable: true })
		{
			return SteamLibraryWidgetState.Empty with { Glyph = glyph };
		}

		var snapshot = source.Snapshot;
		var (status, account) = Resolve(snapshot, options);
		if (status is SteamWidgetStatus.AccountUnavailable)
		{
			return SteamLibraryWidgetState.Empty with { Status = status.Value, Glyph = glyph };
		}

		if (account is { Owned: SteamOwnedState.Private })
		{
			return SteamLibraryWidgetState.Empty with { Status = SteamWidgetStatus.Private, Glyph = glyph };
		}

		if (account is { Owned: SteamOwnedState.Available } owner)
		{
			var recent = owner.OwnedGames.Where(game => game.LastPlayed is not null)
				.Take(MaxRecentGames)
				.Select(game => Game(game.AppId,
					game.Name,
					artwork,
					game.RecentMinutes > 0
						? Strings.RecentPlaytime(Hours(game.RecentMinutes))
						: Strings.TotalPlaytime(Hours(game.PlaytimeMinutes))))
				.ToList();

			return new SteamLibraryWidgetState(SteamWidgetStatus.Ready,
				true,
				owner.OwnedGames.Count,
				Hours(owner.OwnedGames.Sum(game => (long)game.PlaytimeMinutes)),
				Hours(owner.OwnedGames.Sum(game => (long)game.RecentMinutes)),
				recent,
				glyph);
		}

		if (!snapshot.SteamFound)
		{
			return SteamLibraryWidgetState.Empty with { Status = SteamWidgetStatus.NotFound, Glyph = glyph };
		}

		var played = snapshot.Games.Where(game => game.LastPlayed is not null)
			.OrderByDescending(game => game.LastPlayed)
			.Take(MaxRecentGames)
			.Select(game => Game(game.AppId, game.Name, artwork, default))
			.ToList();

		return new SteamLibraryWidgetState(
			snapshot.Games.Count == 0 ? SteamWidgetStatus.NoGames : SteamWidgetStatus.Ready,
			false,
			snapshot.Games.Count,
			string.Empty,
			string.Empty,
			played,
			glyph);
	}

	internal static string Hours(long minutes)
	{
		var hours = minutes / 60d;
		return hours >= 100
			? Math.Round(hours, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture)
			: Math.Round(hours, 1, MidpointRounding.AwayFromZero).ToString("0.#", CultureInfo.InvariantCulture);
	}

	private static (SteamWidgetStatus? Status, SteamAccountSnapshot? Account) Resolve(
		SteamSnapshot snapshot,
		SteamAccountOptions options)
	{
		var account = options.AccountId is { } accountId
			? snapshot.Accounts.FirstOrDefault(candidate => candidate.EntryId == accountId)
			: snapshot.Accounts.FirstOrDefault(candidate => candidate.HasWebApi);

		if (options.AccountId is not null && account is null)
		{
			return (SteamWidgetStatus.AccountUnavailable, null);
		}

		if (account is null || !account.HasWebApi)
		{
			return (SteamWidgetStatus.NeedsWebApi, null);
		}

		return account.Web == SteamWebState.Connected
			? (null, account)
			: (SteamWidgetStatus.AccountUnavailable, account);
	}

	private static SteamLibraryGame Game(int appId, string name, SteamArtworkCache artwork, LocalizedText detail)
	{
		artwork.Track(appId);
		return new SteamLibraryGame($"game-{appId.ToString(CultureInfo.InvariantCulture)}",
			appId,
			name,
			artwork.Find(appId),
			detail);
	}

	private static LocalizedText Status(SteamFriend friend)
	{
		return friend.GameName is { Length: > 0 } game
			? game
			: friend.PersonaState switch
			{
				SteamPersona.Busy => Strings.Persona.Busy(),
				SteamPersona.Away => Strings.Persona.Away(),
				SteamPersona.Snooze => Strings.Persona.Snooze(),
				SteamPersona.LookingToTrade => Strings.Persona.LookingToTrade(),
				SteamPersona.LookingToPlay => Strings.Persona.LookingToPlay(),
				SteamPersona.Offline => Strings.Persona.Offline(),
				_ => Strings.Persona.Online()
			};
	}

	private static string Initial(string name)
	{
		return name.Trim() is { Length: > 0 } trimmed
			? char.IsSurrogate(trimmed[0]) && trimmed.Length > 1
				? trimmed[..2]
				: trimmed[..1].ToUpperInvariant()
			: "?";
	}
}
