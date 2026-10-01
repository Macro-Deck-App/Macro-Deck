using MacroDeckHost.Application.Twitch.Chat;

namespace MacroDeckHost.Application.Twitch.Stats;

public static class TwitchStatsWidgetType
{
	public const string OwnerId = TwitchChatWidgetType.OwnerId;

	public const string LocalId = "stats";

	public const string QualifiedId = OwnerId + "::" + LocalId;

	public const string AccountKey = "account";

	public const string StyleKey = "style";

	public const string MetricKey = "metric";

	public const string TilesKey = "tiles";

	public const string DetailsKey = "details";

	public const string ThumbnailKey = "showThumbnail";

	public const string DefaultStyle = "overview";

	public const string DefaultMetric = "viewers";
}
