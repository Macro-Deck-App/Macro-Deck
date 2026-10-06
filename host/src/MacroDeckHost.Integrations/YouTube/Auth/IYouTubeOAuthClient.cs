namespace MacroDeckHost.Integrations.YouTube.Auth;

internal interface IYouTubeOAuthClient : IDisposable
{
	Task<YouTubeDeviceCode> RequestDeviceCodeAsync(string clientId, CancellationToken cancellationToken);

	Task<YouTubeTokenPoll> PollTokenAsync(
		string clientId,
		string clientSecret,
		string deviceCode,
		CancellationToken cancellationToken);

	Task<YouTubeTokens> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken);
}
