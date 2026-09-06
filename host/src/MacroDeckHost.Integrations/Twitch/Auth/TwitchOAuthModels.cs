namespace MacroDeckHost.Integrations.Twitch.Auth;

internal sealed record TwitchDeviceCode(
	string DeviceCode,
	string UserCode,
	string VerificationUri,
	TimeSpan ExpiresIn,
	TimeSpan Interval);

internal enum TwitchTokenPollStatus
{
	Success,

	Pending,

	SlowDown,

	Expired,

	Denied
}

internal sealed record TwitchTokenPollResult(TwitchTokenPollStatus Status, TwitchTokens? Tokens = null);

internal sealed record TwitchTokens(
	string AccessToken,
	string RefreshToken,
	DateTimeOffset ExpiresAt,
	IReadOnlyList<string> Scopes);

internal sealed record TwitchTokenIdentity(
	string UserId,
	string Login,
	string ClientId,
	IReadOnlyList<string> Scopes,
	TimeSpan ExpiresIn);
