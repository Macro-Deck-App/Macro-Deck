using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Auth;

public static class AuthDefaults
{
	public const string PolicySchemeName = "MacroDeck";
	public const string LoopbackScheme = "Loopback";

	public const string Issuer = "macro-deck";
	public const string Audience = "macro-deck";

	public const string ScopeClaim = "scope";
	public const string AdminScope = "admin";
	public const string ClientScope = "client";

	public const string DeviceClaim = "device";

	public const string RefreshCookie = "md_refresh";
	public const string AccessCookie = "md_access";

	/// <summary>
	/// The cookie name for the listener a request arrived on. Cookies ignore the port, so two hosts on
	/// one machine - a development build beside an installed one - would otherwise share
	/// <see cref="AccessCookie"/> and overwrite each other's, leaving the loser's own
	/// <c>&lt;img&gt;</c> requests carrying a token signed with the wrong key.
	/// </summary>
	public static string AccessCookieFor(int port) => $"{AccessCookie}_{port}";

	/// <summary>Same reasoning as <see cref="AccessCookieFor"/>, for the refresh cookie.</summary>
	public static string RefreshCookieFor(int port) => $"{RefreshCookie}_{port}";

	public const int MinPasswordLength = 8;
	public const int MaxUsernameLength = 64;

	/// <summary>
	/// The admin surface's access token lifetime, and the one a session without a device gets. Short
	/// because an admin token is the account: it configures the host and installs plugins.
	/// </summary>
	public static readonly TimeSpan AdminAccessTokenLifetime = TimeSpan.FromMinutes(15);

	/// <summary>
	/// A deck device's access token lifetime. Long on purpose: these are wall-mounted tablets and phones
	/// on the local network, and every refresh they avoid is a rotation that cannot go wrong. Safe only
	/// because <see cref="DeviceSessionGuard" /> refuses a signed-out device's token on the next request
	/// rather than waiting for it to expire.
	/// </summary>
	public static readonly TimeSpan ClientAccessTokenLifetime = TimeSpan.FromDays(60);

	public static TimeSpan AccessTokenLifetimeFor(AuthScope scope)
		=> scope == AuthScope.Admin ? AdminAccessTokenLifetime : ClientAccessTokenLifetime;

	public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(365);

	// Reuse detection only sees a rotated token while its row exists: this is that window. See ADR 0083.
	public static readonly TimeSpan RevokedRefreshTokenRetention = TimeSpan.FromDays(30);

	// The just-rotated token is accepted once this soon after rotation, so a lost response does not sign out
	// every device. Accepted cost: a stolen token replayed this soon, before its owner, is not detected.
	public static readonly TimeSpan RefreshTokenReuseGrace = TimeSpan.FromSeconds(60);

	public static readonly TimeSpan PairingCodeLifetime = TimeSpan.FromMinutes(15);

	/// <summary>
	/// How long a device-enrollment credential stays valid. Short on purpose: setup restarts the
	/// device's browser as its last act, so the credential is spent seconds after it is minted, and
	/// anything longer is only a wider window for one sitting in a file.
	/// </summary>
	public static readonly TimeSpan DeviceEnrollmentLifetime = TimeSpan.FromMinutes(15);

	public static string ScopeClaimValue(AuthScope scope)
		=> scope == AuthScope.Admin ? AdminScope : ClientScope;
}
