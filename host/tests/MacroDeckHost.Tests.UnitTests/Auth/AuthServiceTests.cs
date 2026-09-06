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

	[SetUp]
	public void SetUp()
	{
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
}
