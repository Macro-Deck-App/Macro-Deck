namespace MacroDeckHost.Integrations.Twitch.Auth;

internal interface ITwitchOAuthClient : IDisposable
{
	Task<TwitchDeviceCode> RequestDeviceCodeAsync(string clientId, string scopes, CancellationToken cancellationToken);

	Task<TwitchTokenPollResult> PollTokenAsync(
		string clientId,
		string scopes,
		string deviceCode,
		CancellationToken cancellationToken);

	Task<TwitchTokens> RefreshAsync(string clientId, string refreshToken, CancellationToken cancellationToken);

	Task<TwitchTokenIdentity> ValidateAsync(string accessToken, CancellationToken cancellationToken);
}
