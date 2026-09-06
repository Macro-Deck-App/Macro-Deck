namespace MacroDeckHost.Integrations.Spotify;

internal static class SpotifyConfigKeys
{
	public const string ClientId = "clientId";
	public const string ClientSecret = "clientSecret";
	public const string AccessToken = "accessToken";
	public const string RefreshToken = "refreshToken";
	public const string ExpiresAt = "expiresAt";
	public const string Scope = "scope";
	public const string DisplayName = "displayName";

	// An open rate-limit episode. Persisted because Spotify's Retry-After routinely outlives the host
	// process, and resuming the poll into an active penalty is what extends it (see SpotifyApiLimitStore).
	public const string ApiLimitKind = "apiLimitKind";
	public const string ApiLimitSince = "apiLimitSince";
	public const string ApiLimitUntil = "apiLimitUntil";
}
