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

	public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);
	public static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromDays(30);

	/// <summary>
	/// How long a device-enrollment credential stays valid. Short on purpose: setup restarts the
	/// device's browser as its last act, so the credential is spent seconds after it is minted, and
	/// anything longer is only a wider window for one sitting in a file.
	/// </summary>
	public static readonly TimeSpan DeviceEnrollmentLifetime = TimeSpan.FromMinutes(15);

	public static string ScopeClaimValue(AuthScope scope)
		=> scope == AuthScope.Admin ? AdminScope : ClientScope;
}
