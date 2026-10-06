namespace MacroDeckHost.Integrations.YouTube.Auth;

internal sealed record YouTubeDeviceCode(
	string DeviceCode,
	string UserCode,
	string VerificationUrl,
	TimeSpan ExpiresIn,
	TimeSpan Interval);

internal enum YouTubeTokenPollStatus
{
	Success,

	Pending,

	SlowDown,

	Denied,

	Expired,

	Rejected
}

internal sealed record YouTubeTokenPoll(
	YouTubeTokenPollStatus Status,
	YouTubeTokens? Tokens = null,
	string? ErrorCode = null);

internal sealed record YouTubeTokens(
	string AccessToken,
	string RefreshToken,
	DateTimeOffset ExpiresAt,
	IReadOnlyList<string> Scopes);
