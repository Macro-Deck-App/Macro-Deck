using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;

namespace MacroDeckHost.Application.Auth;

public class AuthService : IAuthService
{
	private readonly IUserRepository _userRepository;
	private readonly IRefreshTokenRepository _refreshTokenRepository;
	private readonly IPasswordHasher _passwordHasher;
	private readonly IAccessTokenIssuer _accessTokenIssuer;
	private readonly IDeviceService _deviceService;
	private readonly IDeviceEnrollmentStore _deviceEnrollments;
	private readonly PairingCodeStore _pairingCodes;
	private readonly IAppPreferenceService _appPreferences;
	private readonly TimeProvider _timeProvider;

	public AuthService(
		IUserRepository userRepository,
		IRefreshTokenRepository refreshTokenRepository,
		IPasswordHasher passwordHasher,
		IAccessTokenIssuer accessTokenIssuer,
		IDeviceService deviceService,
		IDeviceEnrollmentStore deviceEnrollments,
		PairingCodeStore pairingCodes,
		IAppPreferenceService appPreferences,
		TimeProvider timeProvider)
	{
		_userRepository = userRepository;
		_refreshTokenRepository = refreshTokenRepository;
		_passwordHasher = passwordHasher;
		_accessTokenIssuer = accessTokenIssuer;
		_deviceService = deviceService;
		_deviceEnrollments = deviceEnrollments;
		_pairingCodes = pairingCodes;
		_appPreferences = appPreferences;
		_timeProvider = timeProvider;
	}

	public Task<bool> IsSetupComplete() => _userRepository.AnyExists();

	public async Task<string?> GetUsername() => (await _userRepository.GetSingle())?.Username;

	public async Task<Result<AuthError>> Setup(string username, string password)
	{
		username = username.Trim();
		if (username.Length is 0 or > AuthDefaults.MaxUsernameLength)
		{
			return Result.Fail(AuthError.ValidationError, "Username must not be empty.");
		}

		if (password.Length < AuthDefaults.MinPasswordLength)
		{
			return Result.Fail(AuthError.ValidationError,
				$"Password must be at least {AuthDefaults.MinPasswordLength} characters long.");
		}

		if (await _userRepository.AnyExists())
		{
			return Result.Fail(AuthError.SetupAlreadyComplete, "A user already exists.");
		}

		// Written before the account exists so that a failure here cannot turn a created account into a
		// failed request. An installation that already has an account never reaches this line, which is
		// what keeps the onboarding wizard away from an existing install after an update.
		await _appPreferences.SetOnboarding(true);

		var now = UtcNow();
		await _userRepository.Create(new UserEntity
		{
			Id = Guid.NewGuid(),
			Username = username,
			PasswordHash = _passwordHasher.Hash(password),
			CreatedAt = now,
			UpdatedAt = now
		});

		return Result.Ok<AuthError>();
	}

	public async Task<Result<DeviceEnrollmentTicket, AuthError>> CreateDeviceEnrollment(TimeSpan lifetime)
	{
		var user = await _userRepository.GetSingle();
		if (user is null)
		{
			return Result.Fail<DeviceEnrollmentTicket, AuthError>(AuthError.InvalidCredentials,
				"No account exists yet.");
		}

		var now = UtcNow();
		_deviceEnrollments.PurgeExpired(now);

		var token = TokenHasher.Generate();
		var expiry = now.Add(lifetime);
		_deviceEnrollments.Add(HashToken(token), expiry);

		return Result.Ok<DeviceEnrollmentTicket, AuthError>(new DeviceEnrollmentTicket(token, expiry));
	}

	public async Task<Result<LoginResult, AuthError>> RedeemDeviceEnrollment(
		string token,
		DeviceRegistration device)
	{
		// An enrollment token is removed before it is judged, so a wrong guess and a right one both spend
		// the attempt and a leaked credential cannot be replayed.
		var now = UtcNow();
		var accepted = PairingCodeStore.IsPairingCodeShape(token)
			? _pairingCodes.TryRedeem(token, now)
			: _deviceEnrollments.TryConsume(HashToken(token), out var expiresAt) && expiresAt >= now;
		if (!accepted)
		{
			return Result.Fail<LoginResult, AuthError>(AuthError.InvalidCredentials,
				"The enrollment credential is not valid.");
		}

		var user = await _userRepository.GetSingle();
		if (user is null)
		{
			return Result.Fail<LoginResult, AuthError>(AuthError.InvalidCredentials, "No account exists yet.");
		}

		await _refreshTokenRepository.DeleteExpired(UtcNow());
		await _deviceService.PurgeStale(UtcNow());

		var registration = await _deviceService.RegisterOrReuse(device, UtcNow());
		var startupProfileId = await _deviceService.ResolveStartupProfileId(registration.Device.Id);

		// Client scope, always: this exists for an appliance that cannot be signed in again by hand,
		// and it must never hand a device the admin surface.
		var (login, _) = await IssueTokens(user, AuthScope.Client, registration.Device.Id);
		return Result.Ok<LoginResult, AuthError>(login with
		{
			DeviceId = registration.Device.Id,
			IssuedDeviceSecret = registration.IssuedSecret,
			StartupProfileId = startupProfileId
		});
	}

	public async Task<Result<LoginResult, AuthError>> Login(
		string username,
		string password,
		AuthScope scope,
		DeviceRegistration? device = null)
	{
		var user = await _userRepository.GetSingle();
		if (user is null ||
			!string.Equals(user.Username, username.Trim(), StringComparison.OrdinalIgnoreCase) ||
			!_passwordHasher.Verify(password, user.PasswordHash))
		{
			return Result.Fail<LoginResult, AuthError>(AuthError.InvalidCredentials, "Invalid username or password.");
		}

		await _refreshTokenRepository.DeleteExpired(UtcNow());

		// Purge before registering, so the device we are about to mint cannot be caught by it.
		await _deviceService.PurgeStale(UtcNow());

		DeviceRegistrationResult? registration = null;
		if (device is not null)
		{
			registration = await _deviceService.RegisterOrReuse(device, UtcNow());
		}

		var startupProfileId = registration is not null
			? await _deviceService.ResolveStartupProfileId(registration.Device.Id)
			: null;

		var (login, _) = await IssueTokens(user, scope, registration?.Device.Id);
		return Result.Ok<LoginResult, AuthError>(login with
		{
			DeviceId = registration?.Device.Id,
			IssuedDeviceSecret = registration?.IssuedSecret,
			StartupProfileId = startupProfileId
		});
	}

	public async Task<Result<LoginResult, AuthError>> Refresh(string rawRefreshToken)
	{
		var token = await _refreshTokenRepository.GetByTokenHash(HashToken(rawRefreshToken));
		var now = UtcNow();
		if (token is null || token.ExpiresAt < now)
		{
			return Result.Fail<LoginResult, AuthError>(AuthError.InvalidRefreshToken, "Invalid refresh token.");
		}

		if (token.RevokedAt is not null)
		{
			if (token.ReplacedById is null)
			{
				return Result.Fail<LoginResult, AuthError>(AuthError.InvalidRefreshToken, "Invalid refresh token.");
			}

			// A rotated-out token coming back means it leaked or the client state diverged:
			// kill every session for the user instead of trusting either party.
			await _refreshTokenRepository.RevokeAllForUser(token.UserId, now);
			return Result.Fail<LoginResult, AuthError>(AuthError.RefreshTokenReused, "Refresh token was already used.");
		}

		var user = await _userRepository.GetSingle();
		if (user is null || user.Id != token.UserId)
		{
			return Result.Fail<LoginResult, AuthError>(AuthError.InvalidRefreshToken, "Invalid refresh token.");
		}

		var result = await IssueTokens(user, token.Scope, token.DeviceId);

		token.RevokedAt = now;
		token.ReplacedById = result.RefreshTokenId;
		await _refreshTokenRepository.Update(token);

		var startupProfileId = token.DeviceId is { } deviceId
			? await _deviceService.ResolveStartupProfileId(deviceId)
			: null;

		return Result.Ok<LoginResult, AuthError>(result.Login with
		{
			DeviceId = token.DeviceId, StartupProfileId = startupProfileId
		});
	}

	public async Task Logout(string rawRefreshToken)
	{
		var token = await _refreshTokenRepository.GetByTokenHash(HashToken(rawRefreshToken));
		if (token is null || token.RevokedAt is not null)
		{
			return;
		}

		token.RevokedAt = UtcNow();
		await _refreshTokenRepository.Update(token);
	}

	public async Task<Result<AuthError>> ChangePassword(string currentPassword, string newPassword)
	{
		if (newPassword.Length < AuthDefaults.MinPasswordLength)
		{
			return Result.Fail(AuthError.ValidationError,
				$"Password must be at least {AuthDefaults.MinPasswordLength} characters long.");
		}

		var user = await _userRepository.GetSingle();
		if (user is null || !_passwordHasher.Verify(currentPassword, user.PasswordHash))
		{
			return Result.Fail(AuthError.InvalidCredentials, "Current password is incorrect.");
		}

		var now = UtcNow();
		user.PasswordHash = _passwordHasher.Hash(newPassword);
		user.UpdatedAt = now;
		await _userRepository.Update(user);
		await _refreshTokenRepository.RevokeAllForUser(user.Id, now);

		return Result.Ok<AuthError>();
	}

	public async Task<Result<AuthError>> ChangeUsername(string currentPassword, string newUsername)
	{
		newUsername = newUsername.Trim();
		if (newUsername.Length is 0 or > AuthDefaults.MaxUsernameLength)
		{
			return Result.Fail(AuthError.ValidationError, "Username must not be empty.");
		}

		var user = await _userRepository.GetSingle();
		if (user is null || !_passwordHasher.Verify(currentPassword, user.PasswordHash))
		{
			return Result.Fail(AuthError.InvalidCredentials, "Current password is incorrect.");
		}

		var now = UtcNow();
		user.Username = newUsername;
		user.UpdatedAt = now;
		await _userRepository.Update(user);
		await _refreshTokenRepository.RevokeAllForUser(user.Id, now);

		return Result.Ok<AuthError>();
	}

	private async Task<(LoginResult Login, Guid RefreshTokenId)> IssueTokens(
		UserEntity user,
		AuthScope scope,
		Guid? deviceId)
	{
		var now = UtcNow();
		var rawRefreshToken = GenerateRefreshToken();

		var entity = new RefreshTokenEntity
		{
			Id = Guid.NewGuid(),
			UserId = user.Id,
			TokenHash = HashToken(rawRefreshToken),
			DeviceId = deviceId,
			Scope = scope,
			ExpiresAt = now.Add(AuthDefaults.RefreshTokenLifetime),
			CreatedAt = now
		};
		await _refreshTokenRepository.Create(entity);

		var accessToken = _accessTokenIssuer.Issue(user.Id, user.Username, scope, deviceId);
		var login = new LoginResult(accessToken.Token,
			accessToken.ExpiresAt,
			rawRefreshToken,
			entity.ExpiresAt,
			scope,
			user.Username);

		return (login, entity.Id);
	}

	private DateTime UtcNow() => _timeProvider.GetUtcNow().UtcDateTime;

	private static string GenerateRefreshToken() => TokenHasher.Generate();

	internal static string HashToken(string rawToken) => TokenHasher.Hash(rawToken);
}
