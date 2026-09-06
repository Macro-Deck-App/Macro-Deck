namespace MacroDeckHost.Integrations.Spotify;

internal enum SpotifyAuthenticationState
{
	Valid,

	TemporarilyUnavailable,

	ReauthorizationRequired
}
