using System.Text.Json;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.TwitchChat;

internal static class TwitchChatWidgetSettings
{
	public const string FontSizeKey = "textSize";

	public const string MessageColorKey = "messageColor";

	public const string NameColorKey = "nameColor";

	public const double MinFontSizePercent = 25;

	public const double MaxFontSizePercent = 300;

	public static string? Account(JsonElement data)
		=> WidgetConfigJson.ReadString(data, TwitchChatWidgetType.AccountKey) is { Length: > 0 } account
			? account
			: null;

	public static string? BackgroundColor(JsonElement data)
		=> WidgetColor.NormalizeBackground(WidgetConfigJson.ReadString(data, "backgroundColor"));

	public static string? MessageColor(JsonElement data)
		=> WidgetColor.Normalize(WidgetConfigJson.ReadString(data, MessageColorKey));

	public static string? NameColor(JsonElement data)
		=> WidgetColor.Normalize(WidgetConfigJson.ReadString(data, NameColorKey));

	public static bool AllowsModeration(JsonElement data)
		=> WidgetConfigJson.ReadBool(data, TwitchChatWidgetType.AllowModerationKey) ?? true;

	public static double FontScale(JsonElement data)
		=> WidgetConfigJson.ReadDouble(data, FontSizeKey) is { } percent
			? Math.Clamp(Math.Round(percent), MinFontSizePercent, MaxFontSizePercent) / 100
			: 1;
}
