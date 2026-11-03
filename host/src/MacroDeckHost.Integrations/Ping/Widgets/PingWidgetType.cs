namespace MacroDeckHost.Integrations.Ping.Widgets;

public static class PingWidgetType
{
	public const string OwnerId = "app.macro-deck.ping";

	public const string LocalId = "ping-status";

	public const string QualifiedId = OwnerId + "::" + LocalId;

	public const string TargetKey = "target";

	public const string DisplayNameKey = "displayName";

	public const string ShowLatencyKey = "showLatency";

	public const string ShowPacketLossKey = "showPacketLoss";

	public const string ShowHistoryKey = "showHistory";

	public const string BackgroundColorKey = "backgroundColor";
}
