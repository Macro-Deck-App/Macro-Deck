using System.Text.Json;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.TwitchChat;

internal static class TwitchChatWidgetSettings
{
	public static string? Account(JsonElement data)
		=> WidgetConfigJson.ReadString(data, TwitchChatWidgetType.AccountKey) is { Length: > 0 } account
			? account
			: null;

	public static bool AllowsModeration(JsonElement data)
		=> WidgetConfigJson.ReadBool(data, TwitchChatWidgetType.AllowModerationKey) ?? true;
}
