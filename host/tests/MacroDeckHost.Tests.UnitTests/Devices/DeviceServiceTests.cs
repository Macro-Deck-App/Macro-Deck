using MacroDeckHost.Infrastructure.Auth;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Devices;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;

namespace MacroDeckHost.Tests.UnitTests.Devices;

public class DeviceServiceTests
{
	private InMemoryDeviceRepository _devices = null!;
	private InMemoryRefreshTokenRepository _tokens = null!;
	private DeviceConnectionTracker _tracker = null!;
	private RecordingUiTransport _transport = null!;
	private RecordingMediator _mediator = null!;
	private ManualTimeProvider _time = null!;
	private FakeProfileRegistry _profileRegistry = null!;
	private FakeDeviceDeckNavigator _deckNavigator = null!;
	private StartupReadiness _readiness = null!;
	private ProviderDevicePresenceTracker _providerPresence = null!;
	private DeviceService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_devices = new InMemoryDeviceRepository();
		_tokens = new InMemoryRefreshTokenRepository();
		_time = new ManualTimeProvider();
		_tracker = new DeviceConnectionTracker(new RecordingEventBus(), _time);
		_transport = new RecordingUiTransport();
		_mediator = new RecordingMediator();
		_profileRegistry = new FakeProfileRegistry();
		_deckNavigator = new FakeDeviceDeckNavigator();
		_readiness = new StartupReadiness();
		_providerPresence = new ProviderDevicePresenceTracker();
		_readiness.MarkCachesReady();
		_readiness.MarkVariablesReady();
		_service = new DeviceService(_devices,
			_tokens,
			_tracker,
			_transport,
			_mediator,
			_time,
			_profileRegistry,
			_deckNavigator,
			_readiness,
			_providerPresence,
			new FakeIntegrationRegistry());
	}

	private async Task<DeviceEntity> SeedDevice(bool withLiveToken)
	{
		var now = _time.GetUtcNow().UtcDateTime;
		var device = new DeviceEntity
		{
			Id = Guid.NewGuid(),
			SecretHash = "hash",
			Name = "Kitchen tablet",
			NameIsCustom = false,
			ClientType = DeviceClientType.WebClient,
			FormFactor = DeviceFormFactor.Tablet,
			LastSeenAt = now,
			CreatedAt = now
		};
		await _devices.Create(device);

		if (withLiveToken)
		{
			await _tokens.Create(new RefreshTokenEntity
			{
				Id = Guid.NewGuid(),
				UserId = Guid.NewGuid(),
				TokenHash = "token-hash-" + device.Id,
				DeviceId = device.Id,
				Scope = AuthScope.Client,
				ExpiresAt = now.AddDays(1),
				CreatedAt = now
			});
		}

		return device;
	}

	[Test]
	public async Task Rename_trims_and_caps_the_name_and_marks_it_custom()
	{
		var device = await SeedDevice(withLiveToken: false);

		var result = await _service.Rename(device.Id, "  " + new string('x', 100) + "  ");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.Name, Has.Length.EqualTo(DeviceDefaults.MaxNameLength));
			Assert.That(result.Data.NameIsCustom, Is.True);
		});
	}

	[Test]
	public async Task Rename_rejects_a_name_that_sanitises_to_empty()
	{
		var device = await SeedDevice(withLiveToken: false);

		var result = await _service.Rename(device.Id, "   ");

		Assert.That(result.Error, Is.EqualTo(DeviceError.ValidationError));
	}

	[Test]
	public void Sanitize_caps_a_name_without_splitting_a_surrogate_pair_at_the_boundary()
	{
		var name = new string('x', DeviceDefaults.MaxNameLength - 1) + "\U0001F600"; // pair lands at 63-64

		var sanitized = DeviceService.Sanitize(name);

		Assert.Multiple(() =>
		{
			Assert.That(sanitized, Has.Length.EqualTo(DeviceDefaults.MaxNameLength - 1));
			Assert.That(sanitized, Does.Not.Contain('\uD83D'), "must not end in a lone high surrogate");
		});
	}

	[Test]
	public async Task Rename_of_a_missing_id_fails_with_not_found()
	{
		var result = await _service.Rename(Guid.NewGuid(), "New name");

		Assert.That(result.Error, Is.EqualTo(DeviceError.NotFound));
	}

	[Test]
	public async Task SetStartupProfile_of_a_missing_id_fails_with_not_found()
	{
		var result = await _service.SetStartupProfile(Guid.NewGuid(), "p1");

		Assert.That(result.Error, Is.EqualTo(DeviceError.NotFound));
	}

	[Test]
	public async Task SetStartupProfile_rejects_a_profile_id_that_does_not_resolve()
	{
		var device = await SeedDevice(withLiveToken: false);

		var result = await _service.SetStartupProfile(device.Id, "does-not-exist");

		Assert.That(result.Error, Is.EqualTo(DeviceError.ValidationError));
	}

	[Test]
	public async Task SetStartupProfile_accepts_a_virtual_profile_id()
	{
		var device = await SeedDevice(withLiveToken: false);
		_profileRegistry.AddProfile("obs::main", "OBS / Main");

		var result = await _service.SetStartupProfile(device.Id, "obs::main");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.StartupProfileId, Is.EqualTo("obs::main"));
		});
	}

	[Test]
	public async Task SetStartupProfile_with_null_clears_the_assignment()
	{
		var device = await SeedDevice(withLiveToken: false);
		_profileRegistry.AddProfile("p1", "Profile One");
		await _service.SetStartupProfile(device.Id, "p1");

		var result = await _service.SetStartupProfile(device.Id, null);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.StartupProfileId, Is.Null);
		});
	}

	[Test]
	public async Task SetStartupProfile_publishes_a_device_changed_notification()
	{
		var device = await SeedDevice(withLiveToken: false);
		_profileRegistry.AddProfile("p1", "Profile One");

		await _service.SetStartupProfile(device.Id, "p1");

		Assert.That(_mediator.Published.OfType<DeviceChangedNotification>().Any(n => n.DeviceId == device.Id),
			Is.True);
	}

	[Test]
	public async Task ResolveStartupProfileId_returns_a_resolvable_assignment()
	{
		var device = await SeedDevice(withLiveToken: false);
		_profileRegistry.AddProfile("p1", "Profile One");
		await _service.SetStartupProfile(device.Id, "p1");

		var result = await _service.ResolveStartupProfileId(device.Id);

		Assert.That(result, Is.EqualTo("p1"));
	}

	[Test]
	public async Task ResolveStartupProfileId_clears_a_deleted_persisted_profile_once_caches_are_ready()
	{
		var device = await SeedDevice(withLiveToken: false);
		_profileRegistry.AddProfile("p1", "Profile One");
		await _service.SetStartupProfile(device.Id, "p1");
		_profileRegistry.RemoveProfile("p1");

		var result = await _service.ResolveStartupProfileId(device.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.Null);
			Assert.That(_devices.Devices.Single(d => d.Id == device.Id).StartupProfileId, Is.Null);
		});
	}

	[Test]
	public async Task ResolveStartupProfileId_keeps_a_deleted_looking_assignment_while_caches_are_not_ready()
	{
		var device = await SeedDevice(withLiveToken: false);
		_profileRegistry.AddProfile("p1", "Profile One");
		await _service.SetStartupProfile(device.Id, "p1");
		_profileRegistry.RemoveProfile("p1");

		var notReadyService = new DeviceService(_devices,
			_tokens,
			_tracker,
			_transport,
			_mediator,
			_time,
			_profileRegistry,
			_deckNavigator,
			new StartupReadiness(),
			_providerPresence,
			new FakeIntegrationRegistry());

		var result = await notReadyService.ResolveStartupProfileId(device.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.Null);
			Assert.That(_devices.Devices.Single(d => d.Id == device.Id).StartupProfileId, Is.EqualTo("p1"));
		});
	}

	[Test]
	public async Task ResolveStartupProfileId_keeps_a_virtual_assignment_whose_integration_is_disabled()
	{
		var device = await SeedDevice(withLiveToken: false);
		_profileRegistry.AddProfile("obs::main", "OBS / Main");
		await _service.SetStartupProfile(device.Id, "obs::main");
		_profileRegistry.RemoveProfile("obs::main"); // simulates the integration being disabled

		var result = await _service.ResolveStartupProfileId(device.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result, Is.Null);
			Assert.That(_devices.Devices.Single(d => d.Id == device.Id).StartupProfileId, Is.EqualTo("obs::main"));
		});
	}

	[Test]
	public async Task ClearStartupProfileAssignments_clears_only_matching_devices_and_notifies_each_once()
	{
		var matching1 = await SeedDevice(withLiveToken: false);
		var matching2 = await SeedDevice(withLiveToken: false);
		var other = await SeedDevice(withLiveToken: false);
		_profileRegistry.AddProfile("p1", "Profile One");
		_profileRegistry.AddProfile("p2", "Profile Two");
		await _service.SetStartupProfile(matching1.Id, "p1");
		await _service.SetStartupProfile(matching2.Id, "p1");
		await _service.SetStartupProfile(other.Id, "p2");
		_mediator.Published.Clear();

		await _service.ClearStartupProfileAssignments("p1");

		Assert.Multiple(() =>
		{
			Assert.That(_devices.Devices.Single(d => d.Id == matching1.Id).StartupProfileId, Is.Null);
			Assert.That(_devices.Devices.Single(d => d.Id == matching2.Id).StartupProfileId, Is.Null);
			Assert.That(_devices.Devices.Single(d => d.Id == other.Id).StartupProfileId, Is.EqualTo("p2"));
			Assert.That(_mediator.Published.OfType<DeviceChangedNotification>().Count(n => n.DeviceId == matching1.Id),
				Is.EqualTo(1));
			Assert.That(_mediator.Published.OfType<DeviceChangedNotification>().Count(n => n.DeviceId == matching2.Id),
				Is.EqualTo(1));
			Assert.That(_mediator.Published.OfType<DeviceChangedNotification>().Any(n => n.DeviceId == other.Id),
				Is.False);
		});
	}

	[Test]
	public async Task GetAll_resolves_the_startup_profile_name()
	{
		var device = await SeedDevice(withLiveToken: false);
		_profileRegistry.AddProfile("p1", "Profile One");
		await _service.SetStartupProfile(device.Id, "p1");

		var dto = (await _service.GetAll()).Single(d => d.Id == device.Id.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(dto.StartupProfileId, Is.EqualTo("p1"));
			Assert.That(dto.StartupProfileName, Is.EqualTo("Profile One"));
		});
	}

	[Test]
	public async Task GetAll_reports_a_null_name_when_the_assignment_does_not_currently_resolve()
	{
		var device = await SeedDevice(withLiveToken: false);
		_profileRegistry.AddProfile("obs::main", "OBS / Main");
		await _service.SetStartupProfile(device.Id, "obs::main");
		_profileRegistry.RemoveProfile("obs::main");

		var dto = (await _service.GetAll()).Single(d => d.Id == device.Id.ToString());

		Assert.Multiple(() =>
		{
			Assert.That(dto.StartupProfileId, Is.EqualTo("obs::main"));
			Assert.That(dto.StartupProfileName, Is.Null);
		});
	}

	[Test]
	public async Task ToDto_resolves_the_startup_profile_name()
	{
		var device = await SeedDevice(withLiveToken: false);
		_profileRegistry.AddProfile("p1", "Profile One");
		await _service.SetStartupProfile(device.Id, "p1");

		var dto = await _service.ToDto(_devices.Devices.Single(d => d.Id == device.Id));

		Assert.That(dto.StartupProfileName, Is.EqualTo("Profile One"));
	}

	[Test]
	public async Task LogoutDevice_revokes_tokens_pushes_the_revoked_event_and_aborts_connections()
	{
		var device = await SeedDevice(withLiveToken: true);
		var aborted = false;
		_tracker.Attach("conn-1", device.Id, () => aborted = true);
		_tracker.Register("conn-1", "tab-1");

		var result = await _service.LogoutDevice(device.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_tokens.Tokens.Single(t => t.DeviceId == device.Id).RevokedAt, Is.Not.Null);
			Assert.That(_transport.GroupMessages.Any(m =>
					m.Group == UiDeviceGroups.For(device.Id) && m.Message is DeviceSessionRevokedEvent),
				Is.True);
			Assert.That(aborted, Is.True);
		});
	}

	[Test]
	public async Task LogoutDevice_of_a_missing_id_fails_with_not_found()
	{
		var result = await _service.LogoutDevice(Guid.NewGuid());

		Assert.That(result.Error, Is.EqualTo(DeviceError.NotFound));
	}

	[Test]
	public async Task RemoveDevice_deletes_the_device_revokes_sessions_and_publishes_only_removed()
	{
		var device = await SeedDevice(withLiveToken: true);
		var other = await SeedDevice(withLiveToken: true);
		var aborted = false;
		_tracker.Attach("conn-1", device.Id, () => aborted = true);
		_tracker.Register("conn-1", "tab-1");

		var result = await _service.RemoveDevice(device.Id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_devices.Devices, Does.Not.Contain(device));
			Assert.That(_devices.Devices, Does.Contain(other));
			Assert.That(_tokens.Tokens.Single(t => t.DeviceId == device.Id).RevokedAt, Is.Not.Null);
			Assert.That(_transport.GroupMessages.Any(m =>
					m.Group == UiDeviceGroups.For(device.Id) && m.Message is DeviceSessionRevokedEvent),
				Is.True);
			Assert.That(aborted, Is.True);
			Assert.That(_mediator.Published.OfType<DeviceRemovedNotification>().Any(n => n.DeviceId == device.Id),
				Is.True);
			Assert.That(_mediator.Published.OfType<DeviceChangedNotification>().Any(n => n.DeviceId == device.Id),
				Is.False);
			Assert.That(_tracker.IsRevoked(device.Id), Is.True);
			Assert.That(_tracker.IsRevoked(other.Id), Is.False);
		});
	}

	[Test]
	public async Task RemoveDevice_of_a_missing_id_has_no_side_effects()
	{
		var id = Guid.NewGuid();

		var result = await _service.RemoveDevice(id);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(DeviceError.NotFound));
			Assert.That(_transport.GroupMessages, Is.Empty);
			Assert.That(_mediator.Published, Is.Empty);
			Assert.That(_tracker.IsRevoked(id), Is.False);
		});
	}

	[Test]
	public async Task OpenProfileOnDevice_of_a_missing_id_fails_with_not_found()
	{
		var result = await _service.OpenProfileOnDevice(Guid.NewGuid(), "p1");

		Assert.That(result.Error, Is.EqualTo(DeviceError.NotFound));
	}

	[Test]
	public async Task OpenProfileOnDevice_of_an_offline_device_fails_with_offline_and_skips_the_navigator()
	{
		var device = await SeedDevice(withLiveToken: false);

		var result = await _service.OpenProfileOnDevice(device.Id, "p1");

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(DeviceError.Offline));
			Assert.That(_deckNavigator.ProfileCalls, Is.Empty);
		});
	}

	[Test]
	public async Task OpenProfileOnDevice_of_an_online_device_with_an_unresolvable_profile_fails_with_not_found()
	{
		var device = await SeedDevice(withLiveToken: false);
		_tracker.Attach("conn-1", device.Id, () => { });
		_tracker.Register("conn-1", "tab-1");
		_deckNavigator.ChangeProfileResult = false;

		var result = await _service.OpenProfileOnDevice(device.Id, "does-not-exist");

		Assert.That(result.Error, Is.EqualTo(DeviceError.NotFound));
	}

	[Test]
	public async Task OpenProfileOnDevice_happy_path_calls_the_navigator_once()
	{
		var device = await SeedDevice(withLiveToken: false);
		_tracker.Attach("conn-1", device.Id, () => { });
		_tracker.Register("conn-1", "tab-1");

		var result = await _service.OpenProfileOnDevice(device.Id, "p1");

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_deckNavigator.ProfileCalls, Has.Count.EqualTo(1));
			Assert.That(_deckNavigator.ProfileCalls.Single(), Is.EqualTo((device.Id, "p1")));
		});
	}

	[Test]
	public async Task A_token_revoked_by_logout_fails_refresh_as_plain_invalid_not_as_reuse()
	{
		var users = new InMemoryUserRepository();
		var authService = new AuthService(users,
			_tokens,
			new FakePasswordHasher(),
			new FakeAccessTokenIssuer(_time),
			_service,
			new DeviceEnrollmentStore(),
			new PairingCodeStore(),
			new FakeOnboardingPreferences(),
			_time);
		await authService.Setup("admin", "password123");
		var device = new DeviceRegistration(null,
			null,
			DeviceClientType.WebClient,
			"My device",
			null,
			null,
			DeviceFormFactor.Desktop,
			null);
		var login = await authService.Login("admin", "password123", AuthScope.Client, device);
		Assert.That(login.Success, Is.True);

		await _service.LogoutDevice(login.Data!.DeviceId!.Value);

		var refreshed = await authService.Refresh(login.Data.RefreshToken);

		Assert.Multiple(() =>
		{
			Assert.That(refreshed.Error, Is.EqualTo(AuthError.InvalidRefreshToken));
			Assert.That(_tokens.Tokens.Count(t => t.RevokedAt is not null), Is.EqualTo(1));
		});
	}
}
