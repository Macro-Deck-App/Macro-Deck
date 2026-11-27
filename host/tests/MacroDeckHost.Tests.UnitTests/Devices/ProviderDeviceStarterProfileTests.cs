using System.Text.Json.Nodes;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Localization;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using DeviceSessionGuard = MacroDeckHost.Application.Auth.DeviceSessionGuard;

namespace MacroDeckHost.Tests.UnitTests.Devices;

[TestFixture]
public class ProviderDeviceStarterProfileTests
{
	private const string ProviderId = "com.example.deck";

	private InMemoryDeviceRepository _devices = null!;
	private ProfileCache _cache = null!;
	private FolderCache _folderCache = null!;
	private RecordingMediator _mediator = null!;
	private LayoutRegistry _layouts = null!;
	private StartupReadiness _readiness = null!;
	private ProviderDeviceGates _gates = null!;
	private IServiceScopeFactory _scopeFactory = null!;
	private bool _profileServiceFails;
	private ProviderDeviceStarterProfiles _starterProfiles = null!;
	private PluginDeviceRegistry _registry = null!;

	[SetUp]
	public async Task SetUp()
	{
		_profileServiceFails = false;
		_devices = new InMemoryDeviceRepository();
		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_folderCache = new FolderCache(_cache);
		_mediator = new RecordingMediator();
		_layouts = new LayoutRegistry(new RecordingMediator());
		_readiness = new StartupReadiness();
		_readiness.MarkCachesReady();
		_gates = new ProviderDeviceGates();

		var secrets = new FakeSecretService();
		var services = new ServiceCollection();
		services.AddSingleton<IDeviceRepository>(_devices);
		services.AddSingleton<IMediator>(_mediator);
		services.AddSingleton<IProfileCache>(_cache);
		services.AddSingleton<IFolderCache>(_folderCache);
		services.AddSingleton<IWidgetTypeRegistry>(new WidgetTypeRegistry(new RecordingMediator()));
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton<IAppPreferenceService>(new FakeLocalizationPreferences());
		services.AddScoped<IProfileService>(_ => _profileServiceFails
			? throw new InvalidOperationException("The profile store is unavailable.")
			: new ProfileService(_cache,
				_folderCache,
				_devices,
				_mediator,
				new WidgetSecretCloner(secrets),
				new WidgetSecretScrubber(secrets),
				new NullWidgetVariableCloner()));
		services.AddScoped<IWidgetService>(_ => new WidgetService(_folderCache,
			_cache,
			_mediator,
			new WidgetSecretScrubber(secrets),
			new WidgetSecretCloner(secrets),
			new NullWidgetVariableCloner(),
			null,
			null,
			_devices));
		_scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		_starterProfiles = NewStarterProfiles();
		_registry = NewRegistry(_starterProfiles);
	}

	[TearDown]
	public void TearDown()
	{
		_starterProfiles.Dispose();
		_cache.Dispose();
	}

	[Test]
	public async Task A_new_device_gets_a_profile_of_its_grid_named_after_it_and_assigned()
	{
		var layoutId = await RegisterLayout(Grid("mk2", 3, 5, 1));

		var registration
			= await _registry.RegisterAsync(ProviderId, Descriptor("SERIAL-1", "Stream Deck MK.2", layoutId));

		var profile = _cache.GetAll().Single();
		var device = DeviceOf(registration);
		await Assert.MultipleAsync(() =>
		{
			Assert.That(profile.Name, Is.EqualTo("Stream Deck MK.2"));
			Assert.That((profile.DefaultRows, profile.DefaultColumns), Is.EqualTo((3, 5)));
			Assert.That(device.StartupProfileId, Is.EqualTo(profile.Id.ToString()));
			Assert.That(_mediator.Published.OfType<DeviceChangedNotification>()
					.Any(notification => notification.DeviceId == device.Id),
				Is.True);
			return Task.CompletedTask;
		});
	}

	[Test]
	public async Task The_starter_profile_has_setup_free_example_widgets_in_its_first_cells()
	{
		var layoutId = await RegisterLayout(Grid("mk2", 3, 5, 1));

		await _registry.RegisterAsync(ProviderId, Descriptor("SERIAL-1", "Stream Deck MK.2", layoutId));

		var widgets = HomeWidgets(_cache.GetAll().Single());
		Assert.Multiple(() =>
		{
			Assert.That(widgets.Select(widget => (widget.Type, widget.PositionX, widget.PositionY)),
				Is.EqualTo(new[]
				{
					(WidgetTypeIds.Clock, 0, 0), (WidgetTypeIds.Stopwatch, 1, 0), (WidgetTypeIds.ActionButton, 2, 0)
				}));
			Assert.That(widgets.All(widget => widget is { Width: 1, Height: 1 }), Is.True);
			Assert.That(JsonNode.Parse(widgets[2].Data!)!["label"]!.GetValue<string>(),
				Is.EqualTo(TestLocalization.Resolve(AppStrings.Profiles.StarterExampleButtonLabel(), "en")));
		});
	}

	[Test]
	public async Task A_grid_smaller_than_the_examples_gets_only_as_many_as_fit()
	{
		var layoutId = await RegisterLayout(Grid("pedal", 1, 2));

		await _registry.RegisterAsync(ProviderId, Descriptor("PEDAL-1", "Pedal", layoutId));

		Assert.That(HomeWidgets(_cache.GetAll().Single()).Select(widget => widget.Type),
			Is.EqualTo(new[] { WidgetTypeIds.Clock, WidgetTypeIds.Stopwatch }));
	}

	[Test]
	public async Task Registering_again_and_after_a_restart_creates_no_second_profile()
	{
		var layoutId = await RegisterLayout(Grid("mk2", 3, 5));
		var descriptor = Descriptor("SERIAL-1", "Stream Deck MK.2", layoutId);

		var registration = await _registry.RegisterAsync(ProviderId, descriptor);
		await _registry.UpdateAsync(ProviderId, descriptor);
		await _registry.RegisterAsync(ProviderId, descriptor);

		using var afterRestart = NewStarterProfiles();
		await NewRegistry(afterRestart).RegisterAsync(ProviderId, descriptor);
		await afterRestart.ProvidePendingAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_cache.GetAll(), Has.Count.EqualTo(1));
			Assert.That(DeviceOf(registration).StartupProfileId, Is.EqualTo(_cache.GetAll().Single().Id.ToString()));
		});
	}

	[Test]
	public async Task A_device_whose_starter_profile_the_user_unassigned_gets_none_again()
	{
		var layoutId = await RegisterLayout(Grid("mk2", 3, 5));
		var descriptor = Descriptor("SERIAL-1", "Stream Deck MK.2", layoutId);
		var registration = await _registry.RegisterAsync(ProviderId, descriptor);

		DeviceOf(registration).StartupProfileId = null;
		await _registry.RegisterAsync(ProviderId, descriptor);

		Assert.Multiple(() =>
		{
			Assert.That(DeviceOf(registration).StartupProfileId, Is.Null);
			Assert.That(_cache.GetAll(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_device_whose_starter_profile_was_deleted_gets_none_again()
	{
		var layoutId = await RegisterLayout(Grid("mk2", 3, 5));
		var descriptor = Descriptor("SERIAL-1", "Stream Deck MK.2", layoutId);
		var registration = await _registry.RegisterAsync(ProviderId, descriptor);
		var starter = _cache.GetAll().Single();

		await _cache.Remove(starter.Id);
		DeviceOf(registration).StartupProfileId = null;
		await _registry.RegisterAsync(ProviderId, descriptor);
		await _starterProfiles.ProvidePendingAsync();

		Assert.Multiple(() =>
		{
			Assert.That(DeviceOf(registration).StartupProfileId, Is.Null);
			Assert.That(_cache.GetAll(), Is.Empty);
		});
	}

	[Test]
	public async Task A_device_registered_before_its_layout_gets_the_profile_once_the_layout_arrives()
	{
		var registration = await _registry.RegisterAsync(ProviderId,
			Descriptor("XL-1", "Stream Deck XL", ProviderId + "::xl"));

		await Assert.MultipleAsync(() =>
		{
			Assert.That(_cache.GetAll(), Is.Empty);
			Assert.That(DeviceOf(registration).StartupProfileId, Is.Null);
			return Task.CompletedTask;
		});

		await RegisterLayout(Grid("xl", 4, 8));
		await new StarterProfileLayoutCatalogHandler(_starterProfiles).Handle(new LayoutCatalogChangedNotification(),
			CancellationToken.None);

		var profile = _cache.GetAll().Single();
		var tracker = new DeviceLayoutConstraintTracker(_scopeFactory);
		await tracker.RefreshAsync();
		var constraint = tracker.Get(profile.Id.ToString());
		Assert.Multiple(() =>
		{
			Assert.That((profile.DefaultRows, profile.DefaultColumns), Is.EqualTo((4, 8)));
			Assert.That(DeviceOf(registration).StartupProfileId, Is.EqualTo(profile.Id.ToString()));
			Assert.That(constraint, Is.Not.Null);
			Assert.That(constraint!.RowsLocked && constraint.ColumnsLocked, Is.True);
			Assert.That((constraint.Rows, constraint.Columns), Is.EqualTo((4, 8)));
		});
	}

	[Test]
	public async Task A_device_without_a_layout_gets_the_first_profile_and_no_new_one()
	{
		var first = await CreateProfile("Default Profile");
		await CreateProfile("Second");

		var registration = await _registry.RegisterAsync(ProviderId, Descriptor("PAD-1", "Pad", null));

		Assert.Multiple(() =>
		{
			Assert.That(DeviceOf(registration).StartupProfileId, Is.EqualTo(first.Id.ToString()));
			Assert.That(_cache.GetAll(), Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task A_device_whose_layout_has_no_single_grid_gets_the_first_profile()
	{
		var first = await CreateProfile("Default Profile");
		var layoutId = await RegisterLayout(new LayoutDescriptor("split",
			"Split",
			[
				new LayoutRegion
					{ Id = "left", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = 2, Columns = 2 } },
				new LayoutRegion
					{ Id = "right", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = 2, Columns = 2 } }
			]));

		var registration = await _registry.RegisterAsync(ProviderId, Descriptor("SPLIT-1", "Split pad", layoutId));

		Assert.Multiple(() =>
		{
			Assert.That(DeviceOf(registration).StartupProfileId, Is.EqualTo(first.Id.ToString()));
			Assert.That(_cache.GetAll(), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_device_known_from_before_the_starter_profiles_gets_nothing()
	{
		var layoutId = await RegisterLayout(Grid("mk2", 3, 5));
		_devices.Devices.Add(new DeviceEntity
		{
			Id = Guid.NewGuid(),
			SecretHash = string.Empty,
			Name = "Stream Deck MK.2",
			ClientType = DeviceClientType.Provider,
			ProviderId = ProviderId,
			ProviderDeviceId = "SERIAL-1",
			CreatedAt = DateTime.UtcNow,
			LastSeenAt = DateTime.UtcNow
		});

		await _registry.RegisterAsync(ProviderId, Descriptor("SERIAL-1", "Stream Deck MK.2", layoutId));
		await _starterProfiles.ProvidePendingAsync();

		Assert.Multiple(() =>
		{
			Assert.That(_devices.Devices.Single().StartupProfileId, Is.Null);
			Assert.That(_cache.GetAll(), Is.Empty);
		});
	}

	[Test]
	public async Task A_device_registered_before_the_caches_are_ready_gets_its_profile_once_they_are()
	{
		_readiness = new StartupReadiness();
		_starterProfiles.Dispose();
		_starterProfiles = NewStarterProfiles();
		_registry = NewRegistry(_starterProfiles);
		var layoutId = await RegisterLayout(Grid("mk2", 3, 5));

		var registration
			= await _registry.RegisterAsync(ProviderId, Descriptor("SERIAL-1", "Stream Deck MK.2", layoutId));
		var before = DeviceOf(registration).StartupProfileId;

		_readiness.MarkCachesReady();
		await _starterProfiles.ProvidePendingAsync();

		Assert.Multiple(() =>
		{
			Assert.That(before, Is.Null);
			Assert.That(DeviceOf(registration).StartupProfileId, Is.EqualTo(_cache.GetAll().Single().Id.ToString()));
			Assert.That(_mediator.Published.OfType<DeviceChangedNotification>().Count(), Is.GreaterThanOrEqualTo(2));
		});
	}

	[Test]
	public async Task A_failing_starter_profile_never_fails_the_registration()
	{
		_profileServiceFails = true;
		var layoutId = await RegisterLayout(Grid("mk2", 3, 5));

		var registration
			= await _registry.RegisterAsync(ProviderId, Descriptor("SERIAL-1", "Stream Deck MK.2", layoutId));

		Assert.Multiple(() =>
		{
			Assert.That(DeviceOf(registration).StartupProfileId, Is.Null);
			Assert.That(_mediator.Published.OfType<DeviceChangedNotification>()
					.Any(notification => notification.DeviceId.ToString() == registration.DeviceId),
				Is.True);
		});
	}

	private ProviderDeviceStarterProfiles NewStarterProfiles()
	{
		return new ProviderDeviceStarterProfiles(_scopeFactory, _gates, _layouts, _readiness, Serilog.Core.Logger.None);
	}

	private PluginDeviceRegistry NewRegistry(ProviderDeviceStarterProfiles starterProfiles)
	{
		var time = new ManualTimeProvider();
		return new PluginDeviceRegistry(_scopeFactory,
			new ProviderDevicePresenceTracker(),
			new DeviceConnectionTracker(new RecordingEventBus(),
				time,
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None)),
			new DeviceSessionGuard(),
			_layouts,
			_gates,
			starterProfiles,
			time);
	}

	private async Task<string> RegisterLayout(LayoutDescriptor layout)
	{
		return (await _layouts.Register(ProviderId, layout)).LayoutId;
	}

	private async Task<ProfileEntity> CreateProfile(string name)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		return (await scope.ServiceProvider.GetRequiredService<IProfileService>().Create(name)).Data!;
	}

	private DeviceEntity DeviceOf(DeviceRegistration registration)
	{
		return _devices.Devices.Single(device => device.Id.ToString() == registration.DeviceId);
	}

	private List<WidgetEntity> HomeWidgets(ProfileEntity profile)
	{
		return
		[
			.. _folderCache.GetFoldersByProfileId(profile.Id)
				.Single(folder => folder.IsDefault)
				.Widgets.OrderBy(widget => widget.PositionY)
				.ThenBy(widget => widget.PositionX)
		];
	}

	private static LayoutDescriptor Grid(string id, int rows, int columns, int? maxWidgetSpan = null)
	{
		return new LayoutDescriptor(id,
			id,
			[
				new LayoutRegion
				{
					Id = "keys",
					Kind = LayoutRegionKinds.Grid,
					Grid = new LayoutGrid
					{
						Rows = rows, Columns = columns, MaxWidgetColumns = maxWidgetSpan, MaxWidgetRows = maxWidgetSpan
					}
				}
			]);
	}

	private static DeviceDescriptor Descriptor(string id, string name, string? layoutReference)
	{
		return new DeviceDescriptor(id,
			name,
			name,
			"Elgato",
			layoutReference,
			new DeviceCapabilities { KeyCount = 15, SupportsImages = true },
			DevicePresence.Online);
	}
}
