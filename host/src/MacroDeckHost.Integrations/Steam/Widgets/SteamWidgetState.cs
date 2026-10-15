using System.Globalization;
using MacroDeck.Localization;
using MacroDeck.Ui.Model.Resources;
using MacroDeckHost.Integrations.Steam.State;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal enum SteamWidgetStatus
{
	Unavailable,

	NotFound,

	NoGameSelected,

	NoGames,

	NoFavorites,

	NeedsWebApi,

	Private,

	AccountUnavailable,

	Idle,

	Ready
}

internal sealed record SteamPlayingState(
	SteamWidgetStatus Status,
	string Title,
	string Elapsed,
	UiResource? Cover,
	UiResource? Glyph)
{
	public static SteamPlayingState Empty { get; }
		= new(SteamWidgetStatus.Unavailable, string.Empty, string.Empty, null, null);
}

internal sealed record SteamLauncherTile(string Key, int AppId, string Name, UiResource? Cover);

internal sealed record SteamLauncherState(
	SteamWidgetStatus Status,
	IReadOnlyList<SteamLauncherTile> Tiles,
	UiResource? Glyph,
	LocalizedText Failure)
{
	public static SteamLauncherState Empty { get; } = new(SteamWidgetStatus.Unavailable, [], null, default);

	public IReadOnlyList<SteamLauncherTile> Row(int skip, int take)
	{
		return [.. Tiles.Skip(skip).Take(take)];
	}

	public bool Equals(SteamLauncherState? other)
	{
		return other is not null
			&& Status == other.Status
			&& Equals(Glyph, other.Glyph)
			&& Failure.Equals(other.Failure)
			&& Tiles.SequenceEqual(other.Tiles);
	}

	public override int GetHashCode()
	{
		return HashCode.Combine(Status, Glyph, Failure, Tiles.Count);
	}
}

internal static class SteamWidgetStates
{
	public static SteamPlayingState Playing(
		ISteamSource? source,
		SteamPlayingOptions options,
		DateTimeOffset now,
		SteamArtworkCache artwork,
		UiResource? glyph)
	{
		if (source is not { IsAvailable: true })
		{
			return SteamPlayingState.Empty with { Glyph = glyph };
		}

		var snapshot = source.Snapshot;
		SteamCurrentGame? game;
		var knownPlace = snapshot.SteamFound;

		if (options.AccountId is { } accountId)
		{
			var account = snapshot.Accounts.FirstOrDefault(candidate => candidate.EntryId == accountId);
			if (account is null || account.HasWebApi && account.Web != SteamWebState.Connected)
			{
				return new SteamPlayingState(SteamWidgetStatus.AccountUnavailable,
					string.Empty,
					string.Empty,
					null,
					glyph);
			}

			game = account.CurrentGame;
			knownPlace = true;
		}
		else
		{
			game = snapshot.CurrentGame;
			knownPlace |= snapshot.Accounts.Any(candidate => candidate.Web == SteamWebState.Connected);
		}

		if (game is { } current)
		{
			artwork.Track(current.AppId);

			return new SteamPlayingState(SteamWidgetStatus.Ready,
				current.Name,
				options.ShowSessionTime ? Elapsed(now - current.StartedAt) : string.Empty,
				artwork.Find(current.AppId),
				glyph);
		}

		return new SteamPlayingState(knownPlace ? SteamWidgetStatus.Idle : SteamWidgetStatus.NotFound,
			string.Empty,
			string.Empty,
			null,
			glyph);
	}

	public static SteamLauncherState Launcher(
		ISteamSource? source,
		SteamLauncherOptions options,
		SteamArtworkCache artwork,
		UiResource? glyph,
		LocalizedText failure)
	{
		if (source is not { IsAvailable: true })
		{
			return SteamLauncherState.Empty with { Glyph = glyph };
		}

		var snapshot = source.Snapshot;

		if (options.Source == SteamWidgetType.GameSource)
		{
			if (options.GameAppId is not { } appId)
			{
				return new SteamLauncherState(SteamWidgetStatus.NoGameSelected, [], glyph, failure);
			}

			var name = snapshot.Games.FirstOrDefault(game => game.AppId == appId)?.Name
				?? appId.ToString(CultureInfo.InvariantCulture);
			return new SteamLauncherState(SteamWidgetStatus.Ready, [Tile(appId, name, artwork)], glyph, failure);
		}

		if (options.Source == SteamWidgetType.FavoritesSource)
		{
			var favorites = options.FavoriteAppIds
				.Select(appId => Tile(appId,
					snapshot.Games.FirstOrDefault(game => game.AppId == appId)?.Name
					?? appId.ToString(CultureInfo.InvariantCulture),
					artwork))
				.ToList();

			return new SteamLauncherState(
				favorites.Count == 0 ? SteamWidgetStatus.NoFavorites : SteamWidgetStatus.Ready,
				favorites,
				glyph,
				failure);
		}

		if (!snapshot.SteamFound)
		{
			return new SteamLauncherState(SteamWidgetStatus.NotFound, [], glyph, failure);
		}

		var recent = snapshot.Games
			.OrderByDescending(game => game.LastPlayed ?? DateTimeOffset.MinValue)
			.ThenBy(game => game.Name, StringComparer.CurrentCultureIgnoreCase)
			.Take(options.Count)
			.Select(game => Tile(game.AppId, game.Name, artwork))
			.ToList();

		return new SteamLauncherState(recent.Count == 0 ? SteamWidgetStatus.NoGames : SteamWidgetStatus.Ready,
			recent,
			glyph,
			failure);
	}

	internal static string Elapsed(TimeSpan duration)
	{
		var total = duration < TimeSpan.Zero ? TimeSpan.Zero : duration;
		var seconds = (int)total.TotalSeconds;

		return total.TotalHours >= 1
			? string.Create(CultureInfo.InvariantCulture,
				$"{(int)total.TotalHours}:{seconds / 60 % 60:00}:{seconds % 60:00}")
			: string.Create(CultureInfo.InvariantCulture, $"{seconds / 60}:{seconds % 60:00}");
	}

	private static SteamLauncherTile Tile(int appId, string name, SteamArtworkCache artwork)
	{
		artwork.Track(appId);
		return new SteamLauncherTile($"game-{appId.ToString(CultureInfo.InvariantCulture)}",
			appId,
			name,
			artwork.Find(appId));
	}
}
