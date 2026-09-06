namespace MacroDeckHost.Integrations.Discord.Rpc;

internal static class DiscordRpcCommands
{
	public const string Authorize = "AUTHORIZE";
	public const string Authenticate = "AUTHENTICATE";
	public const string Subscribe = "SUBSCRIBE";
	public const string Unsubscribe = "UNSUBSCRIBE";
	public const string GetGuilds = "GET_GUILDS";
	public const string GetChannels = "GET_CHANNELS";
	public const string GetVoiceSettings = "GET_VOICE_SETTINGS";
	public const string SetVoiceSettings = "SET_VOICE_SETTINGS";
	public const string SelectVoiceChannel = "SELECT_VOICE_CHANNEL";
	public const string GetSelectedVoiceChannel = "GET_SELECTED_VOICE_CHANNEL";
	public const string SelectTextChannel = "SELECT_TEXT_CHANNEL";
	public const string SetActivity = "SET_ACTIVITY";
}

internal static class DiscordRpcEvents
{
	public const string Ready = "READY";
	public const string Error = "ERROR";
	public const string VoiceSettingsUpdate = "VOICE_SETTINGS_UPDATE";
	public const string VoiceStateCreate = "VOICE_STATE_CREATE";
	public const string VoiceStateUpdate = "VOICE_STATE_UPDATE";
	public const string VoiceStateDelete = "VOICE_STATE_DELETE";
	public const string VoiceChannelSelect = "VOICE_CHANNEL_SELECT";
	public const string VoiceConnectionStatus = "VOICE_CONNECTION_STATUS";
	public const string SpeakingStart = "SPEAKING_START";
	public const string SpeakingStop = "SPEAKING_STOP";
	public const string NotificationCreate = "NOTIFICATION_CREATE";
}

internal static class DiscordScopes
{
	public const string Identify = "identify";
	public const string Rpc = "rpc";
	public const string RpcVoiceRead = "rpc.voice.read";
	public const string RpcVoiceWrite = "rpc.voice.write";
	public const string RpcNotificationsRead = "rpc.notifications.read";

	public static IReadOnlyList<string> Full { get; } =
		[Identify, Rpc, RpcVoiceRead, RpcVoiceWrite, RpcNotificationsRead];

	public static IReadOnlyList<string> Core { get; } = [Identify, Rpc, RpcVoiceRead, RpcVoiceWrite];
}
