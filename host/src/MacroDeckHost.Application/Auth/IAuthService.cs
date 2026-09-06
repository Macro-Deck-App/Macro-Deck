using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Auth;

public record DeviceRegistration(
	Guid? DeviceId,
	string? DeviceSecret,
	DeviceClientType ClientType,
	string? ProposedName,
	string? Platform,
	string? Browser,
	DeviceFormFactor FormFactor,
	string? AppVersion);

public record LoginResult(
	string AccessToken,
	DateTime AccessTokenExpiresAt,
	string RefreshToken,
	DateTime RefreshTokenExpiresAt,
	AuthScope Scope,
	string Username,
	Guid? DeviceId = null,
	string? IssuedDeviceSecret = null,
	string? StartupProfileId = null);

/// <summary>
/// A one-time credential handed to a device that cannot be signed in by hand. Short-lived and
/// single-use: it buys exactly one session and then stops existing.
/// </summary>
public record DeviceEnrollmentTicket(string Token, DateTime ExpiresAt);

public interface IAuthService
{
	Task<bool> IsSetupComplete();

	Task<string?> GetUsername();

	Task<Result<AuthError>> Setup(string username, string password);

	Task<Result<LoginResult, AuthError>> Login(
		string username,
		string password,
		AuthScope scope,
		DeviceRegistration? device = null);

	/// <summary>
	/// Mints a one-time enrollment credential for a device that has no way to accept typed input.
	/// Only ever issues <see cref="AuthScope.Client"/>: a device enrolled this way drives the deck and
	/// nothing else, whatever the caller that minted it was allowed to do.
	/// </summary>
	Task<Result<DeviceEnrollmentTicket, AuthError>> CreateDeviceEnrollment(TimeSpan lifetime);

	/// <summary>
	/// Redeems an enrollment credential for an ordinary client session. The credential is consumed
	/// whether or not this succeeds, so a leaked one cannot be replayed.
	/// </summary>
	Task<Result<LoginResult, AuthError>> RedeemDeviceEnrollment(string token, DeviceRegistration device);

	Task<Result<LoginResult, AuthError>> Refresh(string rawRefreshToken);

	Task Logout(string rawRefreshToken);

	Task<Result<AuthError>> ChangePassword(string currentPassword, string newPassword);

	Task<Result<AuthError>> ChangeUsername(string currentPassword, string newUsername);
}
