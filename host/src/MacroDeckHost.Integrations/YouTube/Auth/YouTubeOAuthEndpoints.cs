namespace MacroDeckHost.Integrations.YouTube.Auth;

internal static class YouTubeOAuthEndpoints
{
	public const string DeviceCode = "https://oauth2.googleapis.com/device/code";
	public const string Token = "https://oauth2.googleapis.com/token";

	public const string DeviceCodeGrantType = "urn:ietf:params:oauth:grant-type:device_code";

	// The device flow refuses youtube.force-ssl; youtube alone covers every call the integration makes.
	public const string Scope = "https://www.googleapis.com/auth/youtube";
}
