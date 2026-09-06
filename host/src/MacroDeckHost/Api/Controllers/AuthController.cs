using System.Globalization;
using MacroDeck.Localization;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Notifications;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Auth;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MacroDeckHost.Api.Controllers;

public record AuthStatusResponse(bool SetupComplete, bool Authenticated, bool Trusted, string? Scope, string? Username);

public record SetupRequest(string Username, string Password);

public record DeviceLoginInfo(
	string? DeviceId,
	string? DeviceSecret,
	string? ClientType,
	string? ProposedName,
	string? Platform,
	string? Browser,
	string? FormFactor,
	string? AppVersion);

public record DeviceCredential(string DeviceId, string? DeviceSecret, string? StartupProfileId = null);

public record DeviceEnrollmentResponse(string Token, string ExpiresAt);

public record RedeemDeviceEnrollmentRequest(string Token, DeviceLoginInfo? Device);

public record LoginRequest(
	string Username,
	string Password,
	string Scope,
	DeviceLoginInfo? Device = null);

public record TokenResponse(
	string AccessToken,
	int ExpiresInSeconds,
	string Scope,
	string Username,
	DeviceCredential? Device = null);

public record ChangePasswordRequest(string CurrentPassword, string NewPassword);

public record ChangeUsernameRequest(string CurrentPassword, string NewUsername);

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
	private const string FailedLoginDedupeKey = "login-failed";
	private const int MaxAttemptedUsernameLength = 64;

	private readonly IAuthService _authService;
	private readonly LoginThrottle _loginThrottle;
	private readonly TimeProvider _timeProvider;
	private readonly IUserNotificationStore _userNotificationStore;
	private readonly FailedLoginNotificationTracker _failedLoginTracker;
	private readonly IAppPreferenceService _preferences;
	private readonly ILocalizationResolver _localization;

	public AuthController(
		IAuthService authService,
		LoginThrottle loginThrottle,
		TimeProvider timeProvider,
		IUserNotificationStore userNotificationStore,
		FailedLoginNotificationTracker failedLoginTracker,
		IAppPreferenceService preferences,
		ILocalizationResolver localization)
	{
		_authService = authService;
		_loginThrottle = loginThrottle;
		_timeProvider = timeProvider;
		_userNotificationStore = userNotificationStore;
		_failedLoginTracker = failedLoginTracker;
		_preferences = preferences;
		_localization = localization;
	}

	[HttpGet("status")]
	[AllowAnonymous]
	public async Task<AuthStatusResponse> Status()
	{
		var setupComplete = await _authService.IsSetupComplete();
		var authenticated = User.Identity?.IsAuthenticated ?? false;
		// Trusted = authenticated by the loopback scheme itself (desktop UI). A cookie- or
		// token-authenticated status is NOT trusted: those clients still manage tokens.
		var trusted = authenticated && User.Identity?.AuthenticationType == AuthDefaults.LoopbackScheme;

		// On the trusted transport the principal is synthetic ("desktop"); report the real
		// account name so the UI (e.g. security settings) shows something meaningful.
		var username = trusted ? await _authService.GetUsername() : User.Identity?.Name;

		return new AuthStatusResponse(setupComplete,
			authenticated,
			trusted,
			authenticated ? User.FindFirst(AuthDefaults.ScopeClaim)?.Value : null,
			authenticated ? username : null);
	}

	[HttpPost("setup")]
	[AllowAnonymous]
	public async Task<IActionResult> Setup(SetupRequest body)
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return Problem(statusCode: StatusCodes.Status403Forbidden,
				title: "Setup is only allowed from the desktop app.");
		}

		var result = await _authService.Setup(body.Username, body.Password);
		if (!result.Success)
		{
			return AuthProblem(result.Error, result.ErrorMessage);
		}

		return NoContent();
	}

	[HttpPost("login")]
	[AllowAnonymous]
	public async Task<IActionResult> Login(LoginRequest body)
	{
		if (!TryParseScope(body.Scope, out var scope))
		{
			return Problem(statusCode: StatusCodes.Status400BadRequest, title: "Invalid scope.");
		}

		var throttleKey = $"{HttpContext.Connection.RemoteIpAddress}|{body.Username}";
		if (_loginThrottle.IsThrottled(throttleKey, out var retryAfter))
		{
			Response.Headers.RetryAfter
				= ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
			return Problem(statusCode: StatusCodes.Status429TooManyRequests,
				title: "Too many failed login attempts. Try again later.");
		}

		var result = await _authService.Login(body.Username,
			body.Password,
			scope,
			ToDeviceRegistration(body.Device));
		if (!result.Success || result.Data is null)
		{
			_loginThrottle.RegisterFailure(throttleKey);

			var remoteIp = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
			var attemptedUsername = Truncate(body.Username, MaxAttemptedUsernameLength);
			var failureCount = _failedLoginTracker.RegisterFailure();
			var culture = (await _preferences.GetLocalization()).Culture;
			_userNotificationStore.Raise(new UserNotificationDraft
			{
				Severity = UserNotificationSeverity.Warning,
				Kind = UserNotificationKind.Security,
				Title = _localization.Resolve(AppStrings.Notifications.FailedLogin(), culture),
				Message = _localization.Resolve(AppStrings.Notifications.FailedLoginMessage(count: failureCount,
						username: attemptedUsername,
						ip: remoteIp),
					culture),
				Action = new UserNotificationAction(UserNotificationActionKind.OpenLogs, null),
				DedupeKey = FailedLoginDedupeKey
			});

			return AuthProblem(result.Error, result.ErrorMessage);
		}

		_loginThrottle.RegisterSuccess(throttleKey);
		_failedLoginTracker.Reset();
		SetAuthCookies(result.Data);

		return Ok(ToTokenResponse(result.Data));
	}

	/// <summary>
	/// Mints a one-time credential for a device that cannot be signed in by hand - a screen with no
	/// keyboard. Loopback-only for the same reason setup is: whoever asks for one is handing a device
	/// a session, and that has to be someone sitting at this machine, not a caller on the network.
	/// </summary>
	[HttpPost("device-enrollment")]
	public async Task<IActionResult> CreateDeviceEnrollment()
	{
		if (!LoopbackConnection.IsTrusted(HttpContext))
		{
			return Problem(statusCode: StatusCodes.Status403Forbidden,
				title: "Device enrollment is only allowed from the desktop app.");
		}

		var result = await _authService.CreateDeviceEnrollment(AuthDefaults.DeviceEnrollmentLifetime);
		if (!result.Success || result.Data is null)
		{
			return AuthProblem(result.Error, result.ErrorMessage);
		}

		return Ok(new DeviceEnrollmentResponse(result.Data.Token, result.Data.ExpiresAt.ToString("O")));
	}

	/// <summary>
	/// Spends an enrollment credential for an ordinary client session. Anonymous by necessity - the
	/// device has nothing else to present - but the credential is single-use, short-lived and only
	/// ever buys client scope.
	/// </summary>
	[HttpPost("device-enrollment/redeem")]
	[AllowAnonymous]
	public async Task<IActionResult> RedeemDeviceEnrollment(RedeemDeviceEnrollmentRequest body)
	{
		var throttleKey = $"{HttpContext.Connection.RemoteIpAddress}|device-enrollment";
		if (_loginThrottle.IsThrottled(throttleKey, out var retryAfter))
		{
			Response.Headers.RetryAfter
				= ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
			return Problem(statusCode: StatusCodes.Status429TooManyRequests,
				title: "Too many failed enrollment attempts. Try again later.");
		}

		var result = await _authService.RedeemDeviceEnrollment(body.Token ?? string.Empty,
			ToDeviceRegistration(body.Device) ?? UnknownDeviceRegistration());
		if (!result.Success || result.Data is null)
		{
			_loginThrottle.RegisterFailure(throttleKey);
			return AuthProblem(result.Error, result.ErrorMessage);
		}

		_loginThrottle.RegisterSuccess(throttleKey);
		SetAuthCookies(result.Data);

		return Ok(ToTokenResponse(result.Data));
	}

	[HttpPost("refresh")]
	[AllowAnonymous]
	public async Task<IActionResult> Refresh()
	{
		var rawToken = AuthCookies.ReadRefreshToken(HttpContext);
		if (string.IsNullOrEmpty(rawToken))
		{
			return Problem(statusCode: StatusCodes.Status401Unauthorized, title: "No refresh token.");
		}

		var result = await _authService.Refresh(rawToken);
		if (!result.Success || result.Data is null)
		{
			DeleteAuthCookies();
			return AuthProblem(result.Error, result.ErrorMessage);
		}

		SetAuthCookies(result.Data);

		return Ok(ToTokenResponse(result.Data));
	}

	[HttpPost("logout")]
	[AllowAnonymous]
	public async Task<IActionResult> Logout()
	{
		var rawToken = AuthCookies.ReadRefreshToken(HttpContext);
		if (!string.IsNullOrEmpty(rawToken))
		{
			await _authService.Logout(rawToken);
		}

		DeleteAuthCookies();

		return NoContent();
	}

	[HttpPost("change-password")]
	[Authorize]
	public async Task<IActionResult> ChangePassword(ChangePasswordRequest body)
	{
		var result = await _authService.ChangePassword(body.CurrentPassword, body.NewPassword);
		if (!result.Success)
		{
			return AuthProblem(result.Error, result.ErrorMessage);
		}

		return NoContent();
	}

	[HttpPost("change-username")]
	[Authorize]
	public async Task<IActionResult> ChangeUsername(ChangeUsernameRequest body)
	{
		var result = await _authService.ChangeUsername(body.CurrentPassword, body.NewUsername);
		if (!result.Success)
		{
			return AuthProblem(result.Error, result.ErrorMessage);
		}

		return NoContent();
	}

	private TokenResponse ToTokenResponse(LoginResult login)
	{
		var expiresIn = (int)(login.AccessTokenExpiresAt - _timeProvider.GetUtcNow().UtcDateTime).TotalSeconds;

		var device = login.DeviceId is { } deviceId
			? new DeviceCredential(deviceId.ToString(), login.IssuedDeviceSecret, login.StartupProfileId)
			: null;

		return new TokenResponse(login.AccessToken,
			Math.Max(expiresIn, 0),
			AuthDefaults.ScopeClaimValue(login.Scope),
			login.Username,
			device);
	}

	/// <summary>A device that told us nothing about itself still gets registered, as an unknown one.</summary>
	private static DeviceRegistration UnknownDeviceRegistration()
		=> new(null, null, DeviceClientType.Unknown, null, null, null, DeviceFormFactor.Unknown, null);

	private static DeviceRegistration? ToDeviceRegistration(DeviceLoginInfo? device)
	{
		if (device is null)
		{
			return null;
		}

		return new DeviceRegistration(Guid.TryParse(device.DeviceId, out var id) ? id : null,
			device.DeviceSecret,
			ParseClientType(device.ClientType),
			device.ProposedName,
			device.Platform,
			device.Browser,
			ParseFormFactor(device.FormFactor),
			device.AppVersion);
	}

	private static DeviceClientType ParseClientType(string? value)
		=> value switch
		{
			"web-client" => DeviceClientType.WebClient,
			"admin-ui" => DeviceClientType.AdminUi,
			"native" => DeviceClientType.Native,
			_ => DeviceClientType.Unknown
		};

	private static DeviceFormFactor ParseFormFactor(string? value)
		=> value switch
		{
			"desktop" => DeviceFormFactor.Desktop,
			"tablet" => DeviceFormFactor.Tablet,
			"phone" => DeviceFormFactor.Phone,
			_ => DeviceFormFactor.Unknown
		};

	private void SetAuthCookies(LoginResult login)
	{
		// Per request, not per configuration: a session established over HTTPS must never ship its
		// cookie back in plaintext, and the same host also serves plain HTTP unless the user chose to
		// replace that listener. The consequence is deliberate - a session started over HTTPS is not
		// carried to the HTTP endpoint (the user signs in again there) - because the asymmetry only
		// ever refuses to downgrade a secure session.
		var secure = Request.IsHttps;

		// Named per listener: cookies ignore the port, so an installed host and a second one on the same
		// machine would otherwise keep overwriting each other's cookie, and whichever wrote last would
		// make the other's <img> requests fail with a token signed by the wrong key.
		Response.Cookies.Append(AuthDefaults.AccessCookieFor(AuthCookies.ListenerPort(HttpContext)),
			login.AccessToken,
			new CookieOptions
			{
				HttpOnly = true,
				Secure = secure,
				SameSite = SameSiteMode.Strict,
				Path = "/",
				Expires = new DateTimeOffset(login.AccessTokenExpiresAt, TimeSpan.Zero)
			});
		Response.Cookies.Append(AuthDefaults.RefreshCookieFor(AuthCookies.ListenerPort(HttpContext)),
			login.RefreshToken,
			new CookieOptions
			{
				HttpOnly = true,
				Secure = secure,
				SameSite = SameSiteMode.Strict,
				Path = "/api/auth",
				Expires = new DateTimeOffset(login.RefreshTokenExpiresAt, TimeSpan.Zero)
			});
	}

	private void DeleteAuthCookies()
	{
		var secure = Request.IsHttps;

		var port = AuthCookies.ListenerPort(HttpContext);

		Response.Cookies.Delete(AuthDefaults.RefreshCookieFor(port),
			new CookieOptions { Path = "/api/auth", Secure = secure });
		Response.Cookies.Delete(AuthDefaults.AccessCookieFor(port), new CookieOptions { Path = "/", Secure = secure });

		// The unsuffixed pair is what installations before the per-listener names used; signing out has to
		// clear it too, or a stale one outlives the session it belonged to.
		Response.Cookies.Delete(AuthDefaults.RefreshCookie,
			new CookieOptions { Path = "/api/auth", Secure = secure });
		Response.Cookies.Delete(AuthDefaults.AccessCookie, new CookieOptions { Path = "/", Secure = secure });
	}

	private ObjectResult AuthProblem(AuthError? error, string? message)
	{
		var statusCode = error switch
		{
			AuthError.ValidationError => StatusCodes.Status400BadRequest,
			AuthError.SetupAlreadyComplete => StatusCodes.Status409Conflict,
			AuthError.Throttled => StatusCodes.Status429TooManyRequests,
			_ => StatusCodes.Status401Unauthorized
		};

		return Problem(statusCode: statusCode, title: message ?? "Authentication failed.");
	}

	private static string Truncate(string value, int maxLength)
		=> value.Length <= maxLength ? value : value[..maxLength];

	private static bool TryParseScope(string value, out AuthScope scope)
	{
		switch (value)
		{
			case AuthDefaults.AdminScope:
				scope = AuthScope.Admin;
				return true;
			case AuthDefaults.ClientScope:
				scope = AuthScope.Client;
				return true;
			default:
				scope = default;
				return false;
		}
	}
}
