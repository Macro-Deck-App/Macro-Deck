namespace MacroDeckHost.Application.Connect;

public enum ConnectAccountStatus
{
	SignedOut,
	SigningIn,
	SignedIn,
	ReauthenticationRequired,
	Suspended
}

public enum ConnectConnectivity
{
	Ok,
	Offline
}

/// <summary>
/// Why the last sign-in attempt ended without a session. Carried as a closed set rather than as text so
/// each client renders it in its own language; <see cref="ConnectSessionSnapshot.Message"/> stays
/// reserved for text only the issuer can produce, such as a suspension reason.
/// </summary>
public enum ConnectSignInFailure
{
	Denied,
	Expired,
	Unreachable,
	Failed
}

public sealed record ConnectSessionSnapshot(
	ConnectAccountStatus Status,
	ConnectConnectivity Connectivity,
	ConnectAccount? Account,
	DateTimeOffset? OfflineSince,
	DateTimeOffset? LastSuccessfulRefreshUtc,
	string? Message,
	ConnectSignInFailure? SignInFailure = null)
{
	public static readonly ConnectSessionSnapshot SignedOut =
		new(ConnectAccountStatus.SignedOut, ConnectConnectivity.Ok, null, null, null, null);
}
