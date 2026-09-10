using MacroDeckHost.Infrastructure.Auth;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeckHost.Domain.Entities;

namespace MacroDeckHost.Tests.UnitTests.Auth;

public class DeviceRegistrationTests
{
	private InMemoryUserRepository _users = null!;
	private InMemoryRefreshTokenRepository _tokens = null!;
	private InMemoryDeviceRepository _devices = null!;
	private FakeAccessTokenIssuer _issuer = null!;
	private ManualTimeProvider _time = null!;
	private FakeProfileRegistry _profileRegistry = null!;
	private AuthService _service = null!;

	private DeviceService CreateDeviceService()
		=> new(_devices,
			_tokens,
			new DeviceConnectionTracker(new RecordingEventBus(), _time),
			new RecordingUiTransport(),
			new RecordingMediator(),
			_time,
			_profileRegistry,
			new FakeDeviceDeckNavigator(),
			CompletedStartupReadiness(),
			new ProviderDevicePresenceTracker(),
			new FakeIntegrationRegistry());

	private static StartupReadiness CompletedStartupReadiness()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();
		return readiness;
	}

	[SetUp]
	public async Task SetUp()
	{
		_users = new InMemoryUserRepository();
		_tokens = new InMemoryRefreshTokenRepository();
		_devices = new InMemoryDeviceRepository();
		_time = new ManualTimeProvider();
		_issuer = new FakeAccessTokenIssuer(_time);
		_profileRegistry = new FakeProfileRegistry();
		_service = new AuthService(_users,
			_tokens,
			new FakePasswordHasher(),
			_issuer,
			CreateDeviceService(),
			new DeviceEnrollmentStore(),
			new PairingCodeStore(),
			new FakeOnboardingPreferences(),
			_time);

		await _service.Setup("admin", "password123");
	}

	[Test]
	public async Task Login_without_a_device_block_creates_no_device_and_no_claim()
	{
		var result = await _service.Login("admin", "password123", AuthScope.Client);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_devices.Devices, Is.Empty);
			Assert.That(result.Data!.DeviceId, Is.Null);
			Assert.That(result.Data.StartupProfileId, Is.Null);
			Assert.That(_issuer.LastDeviceId, Is.Null);
			Assert.That(_tokens.Tokens[0].DeviceId, Is.Null);
		});
	}

	[Test]
	public async Task Login_returns_the_devices_startup_profile_id()
	{
		var first = await Login(Proposal("Chrome on Windows"));
		_profileRegistry.AddProfile("p1", "Profile One");
		await CreateDeviceService().SetStartupProfile(first.DeviceId!.Value, "p1");

		var second = await Login(Proposal("Chrome on Windows", first.DeviceId, first.IssuedDeviceSecret));

		Assert.That(second.StartupProfileId, Is.EqualTo("p1"));
	}

	[Test]
	public async Task Login_returns_null_startup_profile_id_when_it_does_not_currently_resolve()
	{
		var first = await Login(Proposal("Chrome on Windows"));
		_profileRegistry.AddProfile("p1", "Profile One");
		await CreateDeviceService().SetStartupProfile(first.DeviceId!.Value, "p1");
		_profileRegistry.RemoveProfile("p1");

		var second = await Login(Proposal("Chrome on Windows", first.DeviceId, first.IssuedDeviceSecret));

		Assert.That(second.StartupProfileId, Is.Null);
	}

	[Test]
	public async Task Refresh_returns_the_startup_profile_id_for_the_tokens_device()
	{
		var login = await Login(Proposal("Chrome on Windows"));
		_profileRegistry.AddProfile("p1", "Profile One");
		await CreateDeviceService().SetStartupProfile(login.DeviceId!.Value, "p1");

		var refreshed = await _service.Refresh(login.RefreshToken);

		Assert.Multiple(() =>
		{
			Assert.That(refreshed.Success, Is.True);
			Assert.That(refreshed.Data!.StartupProfileId, Is.EqualTo("p1"));
		});
	}

	[Test]
	public async Task First_login_mints_a_device_and_returns_its_secret()
	{
		var result = await Login(Proposal("Chrome on Windows – Desktop"));

		var device = _devices.Devices.Single();

		Assert.Multiple(() =>
		{
			Assert.That(result.DeviceId, Is.EqualTo(device.Id));
			Assert.That(result.IssuedDeviceSecret, Is.Not.Null.And.Not.Empty);
			Assert.That(device.Name, Is.EqualTo("Chrome on Windows – Desktop"));
			Assert.That(device.NameIsCustom, Is.False);
			Assert.That(device.ClientType, Is.EqualTo(DeviceClientType.WebClient));
			Assert.That(_issuer.LastDeviceId, Is.EqualTo(device.Id));
			Assert.That(_tokens.Tokens[^1].DeviceId, Is.EqualTo(device.Id));
		});
	}

	[Test]
	public async Task Second_login_with_the_right_credential_reuses_the_device_and_issues_no_secret()
	{
		var first = await Login(Proposal("Chrome on Windows"));

		var second = await Login(Proposal("Chrome on Windows", first.DeviceId, first.IssuedDeviceSecret));

		Assert.Multiple(() =>
		{
			Assert.That(_devices.Devices, Has.Count.EqualTo(1));
			Assert.That(second.DeviceId, Is.EqualTo(first.DeviceId));
			Assert.That(second.IssuedDeviceSecret, Is.Null);
		});
	}

	[Test]
	public async Task A_wrong_secret_mints_a_new_device_and_leaves_the_original_untouched()
	{
		var first = await Login(Proposal("Chrome on Windows"));
		var original = _devices.Devices.Single();
		var originalHash = original.SecretHash;

		var second = await Login(Proposal("Impostor", first.DeviceId, "deadbeef"));

		Assert.Multiple(() =>
		{
			Assert.That(_devices.Devices, Has.Count.EqualTo(2));
			Assert.That(second.DeviceId, Is.Not.EqualTo(first.DeviceId));
			Assert.That(second.IssuedDeviceSecret, Is.Not.Null);
			Assert.That(original.Name, Is.EqualTo("Chrome on Windows"));
			Assert.That(original.SecretHash, Is.EqualTo(originalHash));
		});
	}

	[Test]
	public async Task An_unknown_device_id_mints_a_new_device()
	{
		var result = await Login(Proposal("Chrome on Windows", Guid.NewGuid(), "deadbeef"));

		Assert.Multiple(() =>
		{
			Assert.That(_devices.Devices, Has.Count.EqualTo(1));
			Assert.That(result.DeviceId, Is.EqualTo(_devices.Devices[0].Id));
		});
	}

	[Test]
	public async Task A_user_renamed_device_keeps_its_name_when_a_new_proposal_arrives()
	{
		var first = await Login(Proposal("Chrome on Windows"));
		var device = _devices.Devices.Single();
		device.Name = "Kitchen tablet";
		device.NameIsCustom = true;

		await Login(Proposal("Firefox on Linux", first.DeviceId, first.IssuedDeviceSecret));

		Assert.Multiple(() =>
		{
			Assert.That(device.Name, Is.EqualTo("Kitchen tablet"));
			Assert.That(device.ProposedName, Is.EqualTo("Firefox on Linux"));
			Assert.That(device.Browser, Is.EqualTo("Firefox"));
		});
	}

	[Test]
	public async Task A_non_custom_name_follows_the_latest_proposal()
	{
		var first = await Login(Proposal("Chrome on Windows"));

		await Login(Proposal("Firefox on Linux", first.DeviceId, first.IssuedDeviceSecret));

		Assert.That(_devices.Devices.Single().Name, Is.EqualTo("Firefox on Linux"));
	}

	[Test]
	public async Task Refresh_carries_the_device_to_the_successor_token_and_reissues_the_claim()
	{
		var login = await Login(Proposal("Chrome on Windows"));

		var refreshed = await _service.Refresh(login.RefreshToken);

		Assert.Multiple(() =>
		{
			Assert.That(refreshed.Success, Is.True);
			Assert.That(refreshed.Data!.DeviceId, Is.EqualTo(login.DeviceId));
			Assert.That(_issuer.LastDeviceId, Is.EqualTo(login.DeviceId));
			Assert.That(_tokens.Tokens[^1].DeviceId, Is.EqualTo(login.DeviceId));
		});
	}

	[Test]
	public async Task Revoking_every_session_leaves_the_device_intact()
	{
		await Login(Proposal("Chrome on Windows"));

		await _service.ChangePassword("password123", "newpassword123");

		Assert.Multiple(() =>
		{
			Assert.That(_devices.Devices, Has.Count.EqualTo(1));
			Assert.That(_tokens.Tokens.All(t => t.RevokedAt is not null), Is.True);
		});
	}

	[Test]
	public async Task Login_after_device_removal_with_the_old_credential_mints_a_new_device_and_invalidates_refresh()
	{
		var first = await Login(Proposal("Chrome on Windows"));
		var removed = await CreateDeviceService().RemoveDevice(first.DeviceId!.Value);

		var refreshed = await _service.Refresh(first.RefreshToken);
		var second = await Login(Proposal("Chrome on Windows", first.DeviceId, first.IssuedDeviceSecret));

		Assert.Multiple(() =>
		{
			Assert.That(removed.Success, Is.True);
			Assert.That(refreshed.Error, Is.EqualTo(AuthError.InvalidRefreshToken));
			Assert.That(second.DeviceId, Is.Not.EqualTo(first.DeviceId));
			Assert.That(_devices.Devices, Has.Count.EqualTo(1));
			Assert.That(_devices.Devices[0].Id, Is.EqualTo(second.DeviceId));
		});
	}

	[Test]
	public async Task Purge_removes_a_stale_device_but_keeps_one_with_a_live_session()
	{
		var live = await Login(Proposal("Live device"));
		var abandoned = await Login(Proposal("Abandoned device"));

		foreach (var token in _tokens.Tokens.Where(t => t.DeviceId == abandoned.DeviceId))
		{
			token.RevokedAt = _time.GetUtcNow().UtcDateTime;
		}

		_time.Advance(DeviceDefaults.StaleDeviceRetention + TimeSpan.FromDays(1));
		foreach (var token in _tokens.Tokens.Where(t => t.DeviceId == live.DeviceId))
		{
			token.ExpiresAt = _time.GetUtcNow().UtcDateTime.AddDays(1);
		}

		await CreateDeviceService().PurgeStale(_time.GetUtcNow().UtcDateTime);

		Assert.That(_devices.Devices.Select(d => d.Id), Is.EquivalentTo(new[] { live.DeviceId!.Value }));
	}

	[Test]
	public async Task A_provider_registered_device_cannot_be_claimed_by_a_client_signing_in()
	{
		var provider = new DeviceEntity
		{
			Id = Guid.NewGuid(),
			// A provider device holds no secret at all, so no presented credential may resolve to it.
			SecretHash = string.Empty,
			Name = "Stream Deck XL",
			ClientType = DeviceClientType.Provider,
			LastSeenAt = _time.GetUtcNow().UtcDateTime,
			CreatedAt = _time.GetUtcNow().UtcDateTime,
			ProviderId = "com.example.deck",
			ProviderDeviceId = "SERIAL-1"
		};
		_devices.Devices.Add(provider);

		var result = await Login(Proposal("Chrome on Windows", provider.Id, string.Empty));

		Assert.Multiple(() =>
		{
			Assert.That(result.DeviceId, Is.Not.EqualTo(provider.Id));
			Assert.That(_devices.Devices.Single(device => device.Id == provider.Id).Name,
				Is.EqualTo("Stream Deck XL"));
		});
	}

	private async Task<LoginResult> Login(DeviceRegistration device)
	{
		var result = await _service.Login("admin", "password123", AuthScope.Client, device);
		Assert.That(result.Success, Is.True);

		return result.Data!;
	}

	private static DeviceRegistration Proposal(string proposedName, Guid? id = null, string? secret = null)
		=> new(id,
			secret,
			DeviceClientType.WebClient,
			proposedName,
			proposedName.Contains("Linux", StringComparison.Ordinal) ? "Linux" : "Windows",
			proposedName.Contains("Firefox", StringComparison.Ordinal) ? "Firefox" : "Chrome",
			DeviceFormFactor.Desktop,
			null);
}
