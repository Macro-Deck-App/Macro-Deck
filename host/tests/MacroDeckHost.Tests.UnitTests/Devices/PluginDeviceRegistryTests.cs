using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using Mediator;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Devices;

/// <summary>
/// The device provider contract from the consumer's side: what a provider registers has to show up in
/// the host's own device model, keep its identity across reconnects and restarts, and stay out of the
/// parts of that model that only apply to a client which signs in.
/// </summary>
public class PluginDeviceRegistryTests
{
	private const string ProviderId = "com.example.deck";

	private InMemoryDeviceRepository _devices = null!;
	private InMemoryRefreshTokenRepository _tokens = null!;
	private ManualTimeProvider _time = null!;
	private DeviceConnectionTracker _tracker = null!;
	private ProviderDevicePresenceTracker _presence = null!;
	private FakeProfileRegistry _profileRegistry = null!;
	private IServiceScopeFactory _scopeFactory = null!;
	private PluginDeviceRegistry _registry = null!;
	private DeviceService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_devices = new InMemoryDeviceRepository();
		_tokens = new InMemoryRefreshTokenRepository();
		_time = new ManualTimeProvider();
		_tracker = new DeviceConnectionTracker(new RecordingEventBus(), _time);
		_presence = new ProviderDevicePresenceTracker();
		_profileRegistry = new FakeProfileRegistry();

		var services = new ServiceCollection();
		services.AddSingleton<IDeviceRepository>(_devices);
		services.AddSingleton<IMediator>(new RecordingMediator());
		_scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		_registry = new PluginDeviceRegistry(_scopeFactory,
			_presence,
			_tracker,
			new LayoutRegistry(new RecordingMediator()),
			_time);
		_service = CreateDeviceService();
	}

	private DeviceService CreateDeviceService()
	{
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		return new DeviceService(_devices,
			_tokens,
			_tracker,
			new RecordingUiTransport(),
			new RecordingMediator(),
			_time,
			_profileRegistry,
			new FakeDeviceDeckNavigator(),
			readiness,
			_presence,
			new FakeIntegrationRegistry());
	}

	private static DeviceDescriptor Descriptor(
		string id = "SERIAL-1",
		string name = "Stream Deck XL",
		DevicePresence presence = DevicePresence.Online)
		=> new(id,
			name,
			"Stream Deck XL",
			"Elgato",
			"com.example.deck::xl",
			new DeviceCapabilities { KeyCount = 32, SupportsImages = true },
			presence);

	[Test]
	public async Task A_registered_device_appears_in_the_device_list_with_its_metadata()
	{
		await _registry.RegisterAsync(ProviderId, Descriptor());

		var device = (await _service.GetAll()).Single();

		Assert.Multiple(() =>
		{
			Assert.That(device.Name, Is.EqualTo("Stream Deck XL"));
			Assert.That(device.ClientType, Is.EqualTo("provider"));
			Assert.That(device.ProviderId, Is.EqualTo(ProviderId));
			Assert.That(device.ProviderDeviceId, Is.EqualTo("SERIAL-1"));
			Assert.That(device.Model, Is.EqualTo("Stream Deck XL"));
			Assert.That(device.Manufacturer, Is.EqualTo("Elgato"));
			Assert.That(device.LayoutReference, Is.EqualTo("com.example.deck::xl"));
			Assert.That(device.Online, Is.True);
		});
	}

	[Test]
	public async Task Registering_the_same_provider_local_id_again_is_the_same_device()
	{
		var first = await _registry.RegisterAsync(ProviderId, Descriptor());
		await _registry.UnregisterAsync(ProviderId, "SERIAL-1");

		var second = await _registry.RegisterAsync(ProviderId, Descriptor(name: "Renamed by the provider"));

		var devices = await _service.GetAll();

		Assert.Multiple(() =>
		{
			Assert.That(second.DeviceId, Is.EqualTo(first.DeviceId));
			Assert.That(devices, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_reconnecting_device_keeps_the_name_and_startup_profile_the_user_chose()
	{
		var registration = await _registry.RegisterAsync(ProviderId, Descriptor());
		var deviceId = Guid.Parse(registration.DeviceId);

		await _service.Rename(deviceId, "Desk deck");
		_profileRegistry.AddProfile("p1", "Profile One");
		await _service.SetStartupProfile(deviceId, "p1");

		await _registry.UnregisterAsync(ProviderId, "SERIAL-1");
		await _registry.RegisterAsync(ProviderId, Descriptor(name: "Stream Deck XL"));

		var device = (await _service.GetAll()).Single();
		var resolved = await _service.ResolveStartupProfileId(deviceId);

		Assert.Multiple(() =>
		{
			Assert.That(device.Name, Is.EqualTo("Desk deck"));
			Assert.That(device.StartupProfileId, Is.EqualTo("p1"));
			Assert.That(resolved, Is.EqualTo("p1"));
		});
	}

	[Test]
	public async Task A_restarted_host_recognises_the_device_the_provider_re_registers()
	{
		var first = await _registry.RegisterAsync(ProviderId, Descriptor());

		// Everything the host holds in memory is gone; only the persisted devices survive.
		var afterRestart = new PluginDeviceRegistry(_scopeFactory,
			new ProviderDevicePresenceTracker(),
			new DeviceConnectionTracker(new RecordingEventBus(), _time),
			new LayoutRegistry(new RecordingMediator()),
			_time);

		var second = await afterRestart.RegisterAsync(ProviderId, Descriptor());

		Assert.That(second.DeviceId, Is.EqualTo(first.DeviceId));
	}

	[Test]
	public async Task Devices_of_different_providers_and_ids_never_collapse_into_one()
	{
		await _registry.RegisterAsync(ProviderId, Descriptor("SERIAL-1"));
		await _registry.RegisterAsync(ProviderId, Descriptor("SERIAL-2"));
		await _registry.RegisterAsync("com.example.other", Descriptor("SERIAL-1"));

		Assert.That(await _service.GetAll(), Has.Count.EqualTo(3));
	}

	[Test]
	public async Task Presence_follows_what_the_provider_reports()
	{
		await _registry.RegisterAsync(ProviderId, Descriptor());

		await _registry.SetPresenceAsync(ProviderId, "SERIAL-1", DevicePresence.Offline);
		var offline = (await _service.GetAll()).Single();

		await _registry.SetPresenceAsync(ProviderId, "SERIAL-1", DevicePresence.Online);
		var online = (await _service.GetAll()).Single();

		Assert.Multiple(() =>
		{
			Assert.That(offline.Online, Is.False);
			Assert.That(online.Online, Is.True);
			Assert.That(online.Name, Is.EqualTo("Stream Deck XL"), "presence must not touch the metadata");
		});
	}

	[Test]
	public async Task Unregistering_takes_the_device_offline_but_keeps_it()
	{
		await _registry.RegisterAsync(ProviderId, Descriptor());

		await _registry.UnregisterAsync(ProviderId, "SERIAL-1");

		var device = (await _service.GetAll()).Single();

		Assert.Multiple(() =>
		{
			Assert.That(device.Online, Is.False);
			Assert.That(device.ProviderDeviceId, Is.EqualTo("SERIAL-1"));
		});
	}

	[Test]
	public async Task A_stopped_provider_takes_every_one_of_its_devices_offline()
	{
		await _registry.RegisterAsync(ProviderId, Descriptor("SERIAL-1"));
		await _registry.RegisterAsync(ProviderId, Descriptor("SERIAL-2"));
		await _registry.RegisterAsync("com.example.other", Descriptor("SERIAL-9"));

		await _registry.UnregisterAllAsync(ProviderId);

		var devices = await _service.GetAll();

		Assert.Multiple(() =>
		{
			Assert.That(devices.Where(device => device.ProviderId == ProviderId).Select(device => device.Online),
				Is.All.False);
			Assert.That(devices.Single(device => device.ProviderId == "com.example.other").Online, Is.True);
		});
	}

	[Test]
	public async Task A_provider_device_has_no_session_to_sign_out_of()
	{
		var registration = await _registry.RegisterAsync(ProviderId, Descriptor());

		var result = await _service.LogoutDevice(Guid.Parse(registration.DeviceId));
		var device = (await _service.GetAll()).Single();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(device.HasActiveSession, Is.False);
		});
	}

	[Test]
	public async Task Purging_stale_devices_never_removes_a_provider_device()
	{
		await _registry.RegisterAsync(ProviderId, Descriptor());
		await _registry.UnregisterAsync(ProviderId, "SERIAL-1");

		_time.Advance(DeviceDefaults.StaleDeviceRetention + TimeSpan.FromDays(1));
		await _service.PurgeStale(_time.GetUtcNow().UtcDateTime);

		Assert.That(await _service.GetAll(), Has.Count.EqualTo(1));
	}

	[Test]
	public async Task The_user_can_still_remove_a_provider_device_for_good()
	{
		var registration = await _registry.RegisterAsync(ProviderId, Descriptor());

		var result = await _service.RemoveDevice(Guid.Parse(registration.DeviceId));
		var devices = await _service.GetAll();

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(devices, Is.Empty);
		});
	}
}
