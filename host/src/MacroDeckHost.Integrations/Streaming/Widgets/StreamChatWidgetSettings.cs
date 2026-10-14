using System.Text.Json;
using MacroDeckHost.Integrations.Ui;

namespace MacroDeckHost.Integrations.Streaming.Widgets;

internal static class StreamChatWidgetSettings
{
	public const string FontSizeKey = StreamChatWidgetType.TextSizeKey;

	public const string MessageColorKey = "messageColor";

	public const string NameColorKey = "nameColor";

	public const double MinFontSizePercent = 25;

	public const double MaxFontSizePercent = 300;

	public static string? Account(JsonElement data)
	{
		return StreamWidgetJson.ReadString(data, StreamChatWidgetType.AccountKey) is { Length: > 0 } account
			? account
			: null;
	}

	public static string? BackgroundColor(JsonElement data)
	{
		return WidgetBackgroundColor.Normalize(StreamWidgetJson.ReadString(data,
			StreamChatWidgetType.BackgroundColorKey));
	}

	public static string? MessageColor(JsonElement data)
	{
		return WidgetBackgroundColor.NormalizeColor(StreamWidgetJson.ReadString(data, MessageColorKey));
	}

	public static string? NameColor(JsonElement data)
	{
		return WidgetBackgroundColor.NormalizeColor(StreamWidgetJson.ReadString(data, NameColorKey));
	}

	public static bool AllowsModeration(JsonElement data)
	{
		return StreamWidgetJson.ReadBool(data, StreamChatWidgetType.AllowModerationKey) ?? true;
	}

	public static double FontScale(JsonElement data)
	{
		return StreamWidgetJson.ReadDouble(data, FontSizeKey) is { } percent
			? Math.Clamp(Math.Round(percent), MinFontSizePercent, MaxFontSizePercent) / 100
			: 1;
	}
}
