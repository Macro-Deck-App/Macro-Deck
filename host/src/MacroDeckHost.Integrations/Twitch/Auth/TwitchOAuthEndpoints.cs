namespace MacroDeckHost.Integrations.Twitch.Auth;

internal static class TwitchOAuthEndpoints
{
	public const string Device = "https://id.twitch.tv/oauth2/device";
	public const string Token = "https://id.twitch.tv/oauth2/token";
	public const string Validate = "https://id.twitch.tv/oauth2/validate";

	public const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

	public const string DeveloperConsole = "https://dev.twitch.tv/console/apps/create";

	public const string MacroDeckClientId = "txagxkbn580ypjaojx6t7ui1b3m4jh";
}
