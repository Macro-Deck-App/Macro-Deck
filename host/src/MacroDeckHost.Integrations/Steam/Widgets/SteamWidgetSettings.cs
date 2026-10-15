using System.Text.Json;
using MacroDeckHost.Integrations.Steam.Native;

namespace MacroDeckHost.Integrations.Steam.Widgets;

internal sealed record SteamPlayingOptions(bool ShowSessionTime, string? AccountId = null);

internal sealed record SteamLauncherOptions(
	string Source,
	int? GameAppId,
	int Count,
	IReadOnlyList<int>? Favorites = null)
{
	public IReadOnlyList<int> FavoriteAppIds => Favorites ?? [];
}

internal sealed record SteamAccountOptions(string? AccountId);

internal static class SteamWidgetSettings
{
	public static SteamPlayingOptions ParsePlaying(JsonElement data)
	{
		return new SteamPlayingOptions(SteamSurfaces.ReadBool(data, SteamWidgetType.ShowSessionTimeKey) ?? true,
			SteamSurfaces.ReadString(data, SteamWidgetType.AccountKey) is { Length: > 0 } account ? account : null);
	}

	public static SteamLauncherOptions ParseLauncher(JsonElement data)
	{
		var source = SteamSurfaces.ReadString(data, SteamWidgetType.SourceKey);
		var game = SteamSurfaces.ReadString(data, SteamWidgetType.GameKey);
		var count = SteamSurfaces.ReadDouble(data, SteamWidgetType.CountKey);

		return new SteamLauncherOptions(
			source is not null && SteamWidgetType.Sources.Contains(source, StringComparer.Ordinal)
				? source
				: SteamWidgetType.RecentSource,
			SteamLauncher.ParseAppId(game),
			count is { } value
				? (int)Math.Clamp(Math.Round(value), SteamWidgetType.MinimumCount, SteamWidgetType.MaximumCount)
				: SteamWidgetType.DefaultCount,
			[
				.. SteamSurfaces.ReadStrings(data, SteamWidgetType.FavoritesKey)
					.Select(SteamLauncher.ParseAppId)
					.OfType<int>()
					.Distinct()
					.Take(SteamWidgetType.MaximumCount)
			]);
	}

	public static SteamAccountOptions ParseAccount(JsonElement data)
	{
		return new SteamAccountOptions(SteamSurfaces.ReadString(data, SteamWidgetType.AccountKey) is
			{ Length: > 0 } account
			? account
			: null);
	}

	public static string? BackgroundColor(JsonElement data)
	{
		return SteamSurfaces.NormalizeBackground(SteamSurfaces.ReadString(data, SteamWidgetType.BackgroundColorKey));
	}
}
