namespace MacroDeckHost.Application.Connect;

public sealed record ConnectTokenResponse(string AccessToken, string RefreshToken, string IdToken, TimeSpan ExpiresIn);

/// <summary>
/// A device authorization as returned by the issuer. <paramref name="VerificationUriComplete"/> already
/// carries <paramref name="UserCode"/>, so the user only has to confirm rather than type it.
/// </summary>
public sealed record ConnectDeviceAuthorization(
	string DeviceCode,
	string UserCode,
	Uri VerificationUri,
	Uri VerificationUriComplete,
	TimeSpan ExpiresIn,
	TimeSpan Interval);

/// <summary>
/// The outcome of one device token poll, per RFC 8628 §3.5. Only protocol answers are represented here -
/// a network failure, a 5xx or a rate limit throws <see cref="ConnectAuthTransientException"/> instead,
/// because those say nothing about whether the user has approved.
/// </summary>
public enum ConnectDevicePollStatus
{
	Success,
	Pending,
	SlowDown,
	Denied,
	Expired
}

public sealed record ConnectDevicePollResult(
	ConnectDevicePollStatus Status,
	ConnectTokenResponse? Tokens = null,
	string? Message = null);

public interface IConnectIdentityClient
{
	/// <summary>Starts a device authorization. Needs no redirect URI, which is what makes signing in
	/// from a UI that is not on the host machine possible at all.</summary>
	Task<ConnectDeviceAuthorization> RequestDeviceAuthorization(CancellationToken cancellationToken);

	/// <summary>
	/// Polls the token endpoint once for a pending device authorization. Never throws for
	/// <c>authorization_pending</c>, <c>slow_down</c>, <c>access_denied</c> or <c>expired_token</c> -
	/// those are the answer, not a failure.
	/// </summary>
	Task<ConnectDevicePollResult> PollDeviceToken(string deviceCode, CancellationToken cancellationToken);

	Task<ConnectTokenResponse> Refresh(string refreshToken, CancellationToken cancellationToken);

	Task Revoke(string refreshToken, CancellationToken cancellationToken);
}
