using System.Globalization;
using MacroDeckHost.Infrastructure.Auth;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;

namespace MacroDeckHost.Tests.UnitTests.Auth;

public class AuthServiceTests
{
	private InMemoryUserRepository _users = null!;
	private InMemoryRefreshTokenRepository _tokens = null!;
	private ManualTimeProvider _time = null!;
	private FakeOnboardingPreferences _preferences = null!;
	private AuthService _service = null!;
	private PairingCodeStore _pairingCodes = null!;

	private static readonly DeviceRegistration _phone = new(null,
		null,
		DeviceClientType.Native,
		"Pixel 8 - Phone",
		"Android",
		null,
		DeviceFormFactor.Phone,
		"1.0.0");

	[SetUp]
	public void SetUp()
	{
		_pairingCodes = new PairingCodeStore();
		_users = new InMemoryUserRepository();
		_tokens = new InMemoryRefreshTokenRepository();
		_time = new ManualTimeProvider();
		_preferences = new FakeOnboardingPreferences();
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		_service = new AuthService(_users,
			_tokens,
			new FakePasswordHasher(),
			new FakeAccessTokenIssuer(_time),
			new DeviceService(new InMemoryDeviceRepository(),
				_tokens,
				new DeviceConnectionTracker(new RecordingEventBus(), _time),
				new RecordingUiTransport(),
				new RecordingMediator(),
				_time,
				new FakeProfileRegistry(),
				new FakeDeviceDeckNavigator(),
				readiness,
				new ProviderDevicePresenceTracker(),
				new FakeIntegrationRegistry()),
			new DeviceEnrollmentStore(),
			_pairingCodes,
			_preferences,
			_time);
	}

	[Test]
	public async Task Setup_creates_the_single_user()
	{
		var result = await _service.Setup("  admin  ", "password123");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_users.User, Is.Not.Null);
			Assert.That(_users.User!.Username, Is.EqualTo("admin"));
			Assert.That(_users.User.PasswordHash, Is.EqualTo("H:password123"));
		});
	}

	[Test]
	public async Task Setup_rejects_short_password_and_empty_username()
	{
		var shortPassword = await _service.Setup("admin", "short");
		var emptyUsername = await _service.Setup("   ", "password123");

		Assert.Multiple(() =>
		{
			Assert.That(shortPassword.Error, Is.EqualTo(AuthError.ValidationError));
			Assert.That(emptyUsername.Error, Is.EqualTo(AuthError.ValidationError));
			Assert.That(_users.User, Is.Null);
		});
	}

	[Test]
	public async Task Setup_fails_when_user_already_exists()
	{
		await _service.Setup("admin", "password123");

		var result = await _service.Setup("other", "password456");

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(AuthError.SetupAlreadyComplete));
			Assert.That(_users.User!.Username, Is.EqualTo("admin"));
		});
	}

	[Test]
	public async Task Setup_arms_the_onboarding_wizard()
	{
		var result = await _service.Setup("admin", "password123");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_preferences.OnboardingPending, Is.True);
		});
	}

	[Test]
	public async Task Setup_arms_no_onboarding_wizard_when_it_is_rejected()
	{
		await _service.Setup("admin", "short");
		await _service.Setup("   ", "password123");

		Assert.That(_preferences.OnboardingPending, Is.False);
	}

	[Test]
	public async Task Setup_cannot_re_arm_an_onboarding_wizard_the_user_already_finished()
	{
		await _service.Setup("admin", "password123");
		_preferences.OnboardingPending = false;

		var result = await _service.Setup("other", "password456");

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(AuthError.SetupAlreadyComplete));
			Assert.That(_preferences.OnboardingPending, Is.False);
		});
	}

	[Test]
	public async Task Login_succeeds_with_correct_credentials_and_is_case_insensitive()
	{
		await _service.Setup("Admin", "password123");

		var result = await _service.Login("admin", "password123", AuthScope.Client);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.Scope, Is.EqualTo(AuthScope.Client));
			Assert.That(result.Data.Username, Is.EqualTo("Admin"));
			Assert.That(result.Data.RefreshToken, Is.Not.Empty);
			Assert.That(_tokens.Tokens, Has.Count.EqualTo(1));
			Assert.That(_tokens.Tokens[0].TokenHash, Is.Not.EqualTo(result.Data.RefreshToken));
			Assert.That(result.Data.RefreshTokenExpiresAt,
				Is.EqualTo(_time.Now.UtcDateTime.Add(AuthDefaults.RefreshTokenLifetime)));
		});
	}

	[Test]
	public async Task Login_fails_with_wrong_password_or_username()
	{
		await _service.Setup("admin", "password123");

		var wrongPassword = await _service.Login("admin", "nope", AuthScope.Client);
		var wrongUsername = await _service.Login("nobody", "password123", AuthScope.Client);

		Assert.Multiple(() =>
		{
			Assert.That(wrongPassword.Error, Is.EqualTo(AuthError.InvalidCredentials));
			Assert.That(wrongUsername.Error, Is.EqualTo(AuthError.InvalidCredentials));
			Assert.That(_tokens.Tokens, Is.Empty);
		});
	}

	[Test]
	public async Task Refresh_rotates_the_token_and_keeps_scope()
	{
		await _service.Setup("admin", "password123");
		var login = await _service.Login("admin", "password123", AuthScope.Client);

		var refreshed = await _service.Refresh(login.Data!.RefreshToken);

		Assert.Multiple(() =>
		{
			Assert.That(refreshed.Success, Is.True);
			Assert.That(refreshed.Data!.Scope, Is.EqualTo(AuthScope.Client));
			Assert.That(refreshed.Data.RefreshToken, Is.Not.EqualTo(login.Data.RefreshToken));
			Assert.That(_tokens.Tokens, Has.Count.EqualTo(2));
			Assert.That(_tokens.Tokens[0].RevokedAt, Is.Not.Null);
			Assert.That(_tokens.Tokens[0].ReplacedById, Is.EqualTo(_tokens.Tokens[1].Id));
		});
	}

	[Test]
	public async Task Refresh_with_unknown_or_expired_token_fails()
	{
		await _service.Setup("admin", "password123");
		var login = await _service.Login("admin", "password123", AuthScope.Client);

		var unknown = await _service.Refresh("does-not-exist");

		_time.Advance(AuthDefaults.RefreshTokenLifetime + TimeSpan.FromMinutes(1));
		var expired = await _service.Refresh(login.Data!.RefreshToken);

		Assert.Multiple(() =>
		{
			Assert.That(unknown.Error, Is.EqualTo(AuthError.InvalidRefreshToken));
			Assert.That(expired.Error, Is.EqualTo(AuthError.InvalidRefreshToken));
		});
	}

	[Test]
	public async Task Refresh_reuse_revokes_every_token_of_the_user()
	{
		await _service.Setup("admin", "password123");
		var login = await _service.Login("admin", "password123", AuthScope.Client);
		var otherSession = await _service.Login("admin", "password123", AuthScope.Admin);
		await _service.Refresh(login.Data!.RefreshToken);

		var reuse = await _service.Refresh(login.Data.RefreshToken);
		var otherAfterReuse = await _service.Refresh(otherSession.Data!.RefreshToken);

		Assert.Multiple(() =>
		{
			Assert.That(reuse.Error, Is.EqualTo(AuthError.RefreshTokenReused));
			Assert.That(otherAfterReuse.Error, Is.EqualTo(AuthError.InvalidRefreshToken));
			Assert.That(_tokens.Tokens.All(t => t.RevokedAt is not null), Is.True);
		});
	}

	[Test]
	public async Task Logout_revokes_the_token_and_is_idempotent()
	{
		await _service.Setup("admin", "password123");
		var login = await _service.Login("admin", "password123", AuthScope.Client);

		await _service.Logout(login.Data!.RefreshToken);
		await _service.Logout(login.Data.RefreshToken);
		await _service.Logout("unknown-token");

		Assert.Multiple(() =>
		{
			Assert.That(_tokens.Tokens[0].RevokedAt, Is.Not.Null);
			Assert.That(_tokens.Tokens, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task ChangePassword_verifies_current_password_and_revokes_all_sessions()
	{
		await _service.Setup("admin", "password123");
		await _service.Login("admin", "password123", AuthScope.Client);

		var wrongCurrent = await _service.ChangePassword("wrong", "newpassword1");
		var tooShort = await _service.ChangePassword("password123", "short");
		var ok = await _service.ChangePassword("password123", "newpassword1");
		var oldLogin = await _service.Login("admin", "password123", AuthScope.Client);
		var newLogin = await _service.Login("admin", "newpassword1", AuthScope.Client);

		Assert.Multiple(() =>
		{
			Assert.That(wrongCurrent.Error, Is.EqualTo(AuthError.InvalidCredentials));
			Assert.That(tooShort.Error, Is.EqualTo(AuthError.ValidationError));
			Assert.That(ok.Success, Is.True);
			Assert.That(_tokens.Tokens[0].RevokedAt, Is.Not.Null);
			Assert.That(oldLogin.Error, Is.EqualTo(AuthError.InvalidCredentials));
			Assert.That(newLogin.Success, Is.True);
		});
	}

	[Test]
	public async Task ChangeUsername_verifies_current_password_and_revokes_all_sessions()
	{
		await _service.Setup("admin", "password123");
		await _service.Login("admin", "password123", AuthScope.Admin);

		var wrongCurrent = await _service.ChangeUsername("wrong", "root");
		var ok = await _service.ChangeUsername("password123", "root");
		var login = await _service.Login("root", "password123", AuthScope.Admin);

		Assert.Multiple(() =>
		{
			Assert.That(wrongCurrent.Error, Is.EqualTo(AuthError.InvalidCredentials));
			Assert.That(ok.Success, Is.True);
			Assert.That(_users.User!.Username, Is.EqualTo("root"));
			Assert.That(_tokens.Tokens[0].RevokedAt, Is.Not.Null);
			Assert.That(login.Success, Is.True);
		});
	}

	[Test]
	public void A_pairing_code_is_six_ascii_digits()
	{
		for (var i = 0; i < 200; i++)
		{
			Assert.That(_pairingCodes.Rotate(_time.Now.UtcDateTime).Code, Does.Match("^[0-9]{6}$"));
		}
	}

	[Test]
	public async Task A_pairing_code_buys_one_client_scope_session_and_is_then_spent()
	{
		await _service.Setup("admin", "password123");
		var code = _pairingCodes.Rotate(_time.Now.UtcDateTime).Code;

		var first = await _service.RedeemDeviceEnrollment(code, _phone);
		var replay = await _service.RedeemDeviceEnrollment(code, _phone);

		Assert.Multiple(() =>
		{
			Assert.That(first.Success, Is.True);
			Assert.That(first.Data!.Scope, Is.EqualTo(AuthScope.Client));
			Assert.That(first.Data.DeviceId, Is.Not.Null);
			Assert.That(replay.Error, Is.EqualTo(AuthError.InvalidCredentials));
		});
	}

	[Test]
	public async Task Opening_the_panel_again_invalidates_the_previous_code_at_once()
	{
		await _service.Setup("admin", "password123");
		var previous = _pairingCodes.Rotate(_time.Now.UtcDateTime).Code;
		var current = _pairingCodes.Rotate(_time.Now.UtcDateTime).Code;

		var withPrevious = await _service.RedeemDeviceEnrollment(previous, _phone);
		var withCurrent = await _service.RedeemDeviceEnrollment(current, _phone);

		Assert.Multiple(() =>
		{
			Assert.That(current, Is.Not.EqualTo(previous));
			Assert.That(withPrevious.Success, Is.False);
			Assert.That(withCurrent.Success, Is.True);
		});
	}

	[Test]
	public async Task A_pairing_code_stays_the_same_until_it_expires_after_fifteen_minutes()
	{
		await _service.Setup("admin", "password123");
		var minted = _pairingCodes.Rotate(_time.Now.UtcDateTime);

		_time.Advance(TimeSpan.FromMinutes(14));
		var beforeExpiry = _pairingCodes.Current(_time.Now.UtcDateTime);
		_time.Advance(TimeSpan.FromMinutes(1));
		var expired = await _service.RedeemDeviceEnrollment(minted.Code, _phone);
		var replacement = _pairingCodes.Current(_time.Now.UtcDateTime);

		Assert.Multiple(() =>
		{
			Assert.That(beforeExpiry, Is.EqualTo(minted));
			Assert.That(minted.ExpiresAt, Is.EqualTo(_time.Now.UtcDateTime));
			Assert.That(expired.Success, Is.False);
			Assert.That(replacement.Code, Is.Not.EqualTo(minted.Code));
		});
	}

	[Test]
	public async Task Five_failed_redeems_from_any_caller_clear_the_code()
	{
		await _service.Setup("admin", "password123");
		var code = _pairingCodes.Rotate(_time.Now.UtcDateTime).Code;

		for (var i = 1; i <= PairingCodeStore.MaxFailures; i++)
		{
			await _service.RedeemDeviceEnrollment(WrongCode(code, i), _phone);
		}

		var correct = await _service.RedeemDeviceEnrollment(code, _phone);
		var next = _pairingCodes.Current(_time.Now.UtcDateTime).Code;

		Assert.Multiple(() =>
		{
			Assert.That(correct.Success, Is.False);
			Assert.That(next, Is.Not.EqualTo(code));
		});
	}

	[Test]
	public async Task Fewer_failed_redeems_than_the_limit_leave_the_code_valid()
	{
		await _service.Setup("admin", "password123");
		var code = _pairingCodes.Rotate(_time.Now.UtcDateTime).Code;

		for (var i = 1; i < PairingCodeStore.MaxFailures; i++)
		{
			await _service.RedeemDeviceEnrollment(WrongCode(code, i), _phone);
		}

		Assert.That((await _service.RedeemDeviceEnrollment(code, _phone)).Success, Is.True);
	}

	[Test]
	public async Task A_burst_of_parallel_guesses_never_gets_more_than_the_failure_limit()
	{
		var now = _time.Now.UtcDateTime;
		var code = _pairingCodes.Rotate(now).Code;

		await Task.WhenAll(Enumerable.Range(1, 50)
			.Select(i => Task.Run(() => _pairingCodes.TryRedeem(WrongCode(code, i), now))));

		Assert.That(_pairingCodes.TryRedeem(code, now), Is.False);
	}

	[Test]
	public async Task Failed_enrollment_tokens_do_not_count_against_the_pairing_code()
	{
		await _service.Setup("admin", "password123");
		var code = _pairingCodes.Rotate(_time.Now.UtcDateTime).Code;

		for (var i = 0; i < PairingCodeStore.MaxFailures * 2; i++)
		{
			await _service.RedeemDeviceEnrollment(TokenHasher.Generate(), _phone);
		}

		Assert.That((await _service.RedeemDeviceEnrollment(code, _phone)).Success, Is.True);
	}

	[Test]
	public async Task No_pairing_code_is_valid_until_the_desktop_mints_one()
	{
		await _service.Setup("admin", "password123");

		var guesses = await Task.WhenAll(Enumerable.Range(0, 3)
			.Select(i => _service.RedeemDeviceEnrollment(i.ToString("D6", CultureInfo.InvariantCulture), _phone)));

		Assert.That(guesses.Select(guess => guess.Success), Is.All.False);
	}

	[Test]
	public async Task A_refresh_token_lives_365_days()
	{
		await _service.Setup("admin", "password123");

		var login = await _service.Login("admin", "password123", AuthScope.Client, _phone);

		Assert.That(login.Data!.RefreshTokenExpiresAt, Is.EqualTo(_time.Now.UtcDateTime.AddDays(365)));
	}

	[Test]
	public async Task A_rotated_refresh_token_presented_again_within_30_days_still_revokes_every_session()
	{
		await _service.Setup("admin", "password123");
		var login = await _service.Login("admin", "password123", AuthScope.Client, _phone);
		var rotated = await _service.Refresh(login.Data!.RefreshToken);

		_time.Advance(TimeSpan.FromDays(29));
		await _service.Login("admin", "password123", AuthScope.Client);
		var reuse = await _service.Refresh(login.Data.RefreshToken);
		var successor = await _service.Refresh(rotated.Data!.RefreshToken);

		Assert.Multiple(() =>
		{
			Assert.That(reuse.Error, Is.EqualTo(AuthError.RefreshTokenReused));
			Assert.That(successor.Success, Is.False);
		});
	}

	[Test]
	public async Task Rotated_refresh_tokens_are_deleted_30_days_after_rotation_while_live_ones_stay()
	{
		await _service.Setup("admin", "password123");
		var login = await _service.Login("admin", "password123", AuthScope.Client, _phone);
		var rotated = await _service.Refresh(login.Data!.RefreshToken);

		_time.Advance(TimeSpan.FromDays(31));
		await _service.Login("admin", "password123", AuthScope.Client);

		Assert.Multiple(() =>
		{
			Assert.That(_tokens.Tokens.Any(t => t.TokenHash == TokenHasher.Hash(login.Data.RefreshToken)), Is.False);
			Assert.That(_tokens.Tokens.Any(t => t.TokenHash == TokenHasher.Hash(rotated.Data!.RefreshToken)), Is.True);
		});
	}

	private static string WrongCode(string code, int offset)
		=> ((int.Parse(code, CultureInfo.InvariantCulture) + offset) % 1_000_000)
			.ToString("D6", CultureInfo.InvariantCulture);
}
