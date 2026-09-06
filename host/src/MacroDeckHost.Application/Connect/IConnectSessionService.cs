namespace MacroDeckHost.Application.Connect;

public interface IConnectSessionService
{
	ConnectSessionSnapshot Current { get; }

	/// <summary>Raised after a real state transition. Never raised for a cached-token access that did
	/// not change the snapshot.</summary>
	event EventHandler<ConnectSessionSnapshot>? SessionChanged;

	/// <summary>
	/// Loads the stored credential, if any, and publishes <see cref="ConnectAccountStatus.SignedIn"/>
	/// immediately from the cached profile. Must not block on network access - the background refresh
	/// this kicks off is fire-and-forget from the caller's point of view.
	/// </summary>
	Task Initialize(CancellationToken cancellationToken = default);

	Task<ConnectSignInStart> StartSignIn(CancellationToken cancellationToken = default);

	Task CancelSignIn(CancellationToken cancellationToken = default);

	Task SignOut(CancellationToken cancellationToken = default);

	/// <summary>
	/// Returns a currently valid access token, refreshing first if necessary. Throws
	/// <see cref="ConnectAuthRejectedException"/> when the status is
	/// <see cref="ConnectAccountStatus.ReauthenticationRequired"/>, and
	/// <see cref="ConnectAccountSuspendedException"/> when it is
	/// <see cref="ConnectAccountStatus.Suspended"/>.
	/// </summary>
	Task<string> GetAccessToken(CancellationToken cancellationToken = default);
}
