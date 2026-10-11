using System.Globalization;
using System.Text.Json;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal sealed record SteamPlayingOptions(bool ShowSessionTime, string? AccountId = null);

internal sealed record SteamLauncherOptions(string Source, int? GameAppId, int Count);

internal static class SteamWidgetSettings
{
	public static SteamPlayingOptions ParsePlaying(JsonElement data)
		=> new(SteamSurfaces.ReadBool(data, SteamWidgetType.ShowSessionTimeKey) ?? true,
			SteamSurfaces.ReadString(data, SteamWidgetType.AccountKey) is { Length: > 0 } account ? account : null);

	public static SteamLauncherOptions ParseLauncher(JsonElement data)
	{
		var source = SteamSurfaces.ReadString(data, SteamWidgetType.SourceKey);
		var game = SteamSurfaces.ReadString(data, SteamWidgetType.GameKey);
		var count = SteamSurfaces.ReadDouble(data, SteamWidgetType.CountKey);

		return new SteamLauncherOptions(
			source is not null && SteamWidgetType.Sources.Contains(source, StringComparer.Ordinal)
				? source
				: SteamWidgetType.RecentSource,
			game is { Length: > 0 and <= 10 } && game.All(char.IsAsciiDigit) &&
			int.TryParse(game, NumberStyles.None, CultureInfo.InvariantCulture, out var appId) && appId > 0
				? appId
				: null,
			count is { } value
				? (int)Math.Clamp(Math.Round(value), SteamWidgetType.MinimumCount, SteamWidgetType.MaximumCount)
				: SteamWidgetType.DefaultCount);
	}

	public static string? BackgroundColor(JsonElement data)
		=> SteamSurfaces.NormalizeBackground(SteamSurfaces.ReadString(data, SteamWidgetType.BackgroundColorKey));
}
