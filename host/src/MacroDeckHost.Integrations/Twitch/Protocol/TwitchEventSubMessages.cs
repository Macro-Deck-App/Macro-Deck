using System.Text.Json;

namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal static class TwitchEventSubMessageTypes
{
	public const string Welcome = "session_welcome";
	public const string Keepalive = "session_keepalive";
	public const string Notification = "notification";
	public const string Reconnect = "session_reconnect";
	public const string Revocation = "revocation";
}

internal sealed record TwitchEventSubMessage(
	string MessageId,
	string MessageType,
	DateTimeOffset Timestamp,
	string? SubscriptionType,
	string? SubscriptionVersion,
	JsonElement Payload);

internal sealed record TwitchSessionWelcome(string SessionId, TimeSpan Keepalive);
