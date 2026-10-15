namespace MacroDeckHost.Integrations.Steam.Widgets;

public static class SteamWidgetType
{
	public const string OwnerId = "app.macro-deck.steam";

	public const string CurrentlyPlayingLocalId = "steam-currently-playing";

	public const string CurrentlyPlayingQualifiedId = OwnerId + "::" + CurrentlyPlayingLocalId;

	public const string GameLauncherLocalId = "steam-game-launcher";

	public const string GameLauncherQualifiedId = OwnerId + "::" + GameLauncherLocalId;

	public const string FriendsLocalId = "steam-friends";

	public const string FriendsQualifiedId = OwnerId + "::" + FriendsLocalId;

	public const string LibraryLocalId = "steam-library";

	public const string LibraryQualifiedId = OwnerId + "::" + LibraryLocalId;

	public const string BackgroundColorKey = "backgroundColor";

	public const string ShowSessionTimeKey = "showSessionTime";

	public const string AccountKey = "account";

	public const string SourceKey = "source";

	public const string GameKey = "game";

	public const string CountKey = "count";

	public const string FavoritesKey = "favorites";

	public const string GameSource = "game";

	public const string RecentSource = "recent";

	public const string FavoritesSource = "favorites";

	public const int MinimumCount = 1;

	public const int MaximumCount = 6;

	public const int DefaultCount = 4;

	public static IReadOnlyList<string> Sources { get; } = [GameSource, RecentSource, FavoritesSource];
}
