namespace MacroDeckHost.Integrations.Discord;

internal static class DiscordEventIds
{
	public const string Connected = "connected";
	public const string Disconnected = "disconnected";
	public const string Muted = "muted";
	public const string Unmuted = "unmuted";
	public const string Deafened = "deafened";
	public const string Undeafened = "undeafened";
	public const string MicrophoneSilenced = "microphone-silenced";
	public const string MicrophoneLive = "microphone-live";
	public const string ServerMuted = "server-muted";
	public const string ServerUnmuted = "server-unmuted";
	public const string ServerDeafened = "server-deafened";
	public const string ServerUndeafened = "server-undeafened";
	public const string VoiceChannelJoined = "voice-channel-joined";
	public const string VoiceChannelLeft = "voice-channel-left";
	public const string VoiceConnectionStateChanged = "voice-connection-state-changed";
	public const string SpeakingStarted = "speaking-started";
	public const string SpeakingStopped = "speaking-stopped";
	public const string NotificationReceived = "notification-received";
}
