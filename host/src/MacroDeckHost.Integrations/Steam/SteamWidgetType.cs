namespace MacroDeckHost.Integrations.Steam;

public static class SteamWidgetType
{
	public const string OwnerId = "app.macro-deck.steam";

	public const string CurrentlyPlayingLocalId = "steam-currently-playing";

	public const string CurrentlyPlayingQualifiedId = OwnerId + "::" + CurrentlyPlayingLocalId;

	public const string GameLauncherLocalId = "steam-game-launcher";

	public const string GameLauncherQualifiedId = OwnerId + "::" + GameLauncherLocalId;

	public const string BackgroundColorKey = "backgroundColor";

	public const string ShowSessionTimeKey = "showSessionTime";

	public const string AccountKey = "account";

	public const string SourceKey = "source";

	public const string GameKey = "game";

	public const string CountKey = "count";

	public const string GameSource = "game";

	public const string RecentSource = "recent";

	public const int MinimumCount = 1;

	public const int MaximumCount = 6;

	public const int DefaultCount = 4;

	public static IReadOnlyList<string> Sources { get; } = [GameSource, RecentSource];
}
