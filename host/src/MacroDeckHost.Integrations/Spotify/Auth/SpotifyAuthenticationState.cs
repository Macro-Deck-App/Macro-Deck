namespace MacroDeckHost.Integrations.Spotify.Auth;

internal enum SpotifyAuthenticationState
{
	Valid,

	TemporarilyUnavailable,

	ReauthorizationRequired
}
