namespace MacroDeckHost.Application.Connect;

/// <summary>
/// A pending device authorization. <see cref="VerificationUriComplete"/> is what a client opens - it
/// already carries the code - while <see cref="VerificationUri"/> and <see cref="UserCode"/> are shown
/// so the user can reach the same page by hand, and can check the code against the one on it.
/// </summary>
public sealed record ConnectSignInStart(
	Uri VerificationUri,
	Uri VerificationUriComplete,
	string UserCode,
	DateTimeOffset ExpiresAtUtc);

public interface IConnectSignInFlow
{
	/// <summary>
	/// Starts (or, when one is already pending, returns the existing) sign-in attempt: requests a device
	/// authorization and starts polling for its outcome. At most one attempt is ever pending; a second
	/// call while one is live returns the same code and expiry idempotently.
	/// </summary>
	Task<ConnectSignInStart> Begin(CancellationToken cancellationToken = default);
}
