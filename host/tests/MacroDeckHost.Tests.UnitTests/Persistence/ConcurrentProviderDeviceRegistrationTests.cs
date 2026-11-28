using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Application.Secrets;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Infrastructure.Caching;
using MacroDeckHost.Infrastructure.Persistence;
using MacroDeckHost.Infrastructure.Persistence.Repositories;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using Mediator;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Persistence;

[TestFixture]
[NonParallelizable]
public class ConcurrentProviderDeviceRegistrationTests
{
	private const string ProviderId = "com.example.deck";

	private string _dataDir = null!;
	private string? _previousDataDir;
	private MacroDeckPaths _paths = null!;
	private ProfileCache _cache = null!;
	private FolderCache _folderCache = null!;
	private LayoutRegistry _layouts = null!;
	private ProviderDeviceStarterProfiles? _starterProfiles;

	[SetUp]
	public async Task SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		_paths = new MacroDeckPaths();
		_paths.EnsureDirectoriesExist();
		DatabaseMigrationHelper.MigrateDatabase(_paths);

		_cache = new ProfileCache(new InMemoryProfileStore(), new LoggerConfiguration().CreateLogger());
		await _cache.InitializeCache();
		_folderCache = new FolderCache(_cache);
		_layouts = new LayoutRegistry(new RecordingMediator());
	}

	[TearDown]
	public void TearDown()
	{
		_starterProfiles?.Dispose();
		_cache.Dispose();
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _previousDataDir);
		SqliteConnection.ClearAllPools();

		if (Directory.Exists(_dataDir))
		{
			Directory.Delete(_dataDir, true);
		}
	}

	[Test]
	public async Task Two_concurrent_registrations_of_a_new_device_are_the_same_device()
	{
		var reads = new OverlappingReads();
		var registry = CreateRegistry(reads);

		var first = Task.Run(() => registry.RegisterAsync(ProviderId, Descriptor("First name")));
		var second = Task.Run(() => registry.RegisterAsync(ProviderId, Descriptor("Second name")));
		var registrations = await Task.WhenAll(first, second);

		await using var context = new DatabaseContext(Log.Logger, _paths);
		var rows = await context.Devices.Where(d => d.ProviderId == ProviderId).ToListAsync();

		Assert.Multiple(() =>
		{
			Assert.That(registrations[1].DeviceId, Is.EqualTo(registrations[0].DeviceId));
			Assert.That(rows, Has.Count.EqualTo(1));
			Assert.That(rows.Single().Id.ToString(), Is.EqualTo(registrations[0].DeviceId));
		});
	}

	[Test]
	public async Task Two_concurrent_registrations_of_a_new_device_create_one_starter_profile()
	{
		var reads = new OverlappingReads();
		var registry = CreateRegistry(reads);
		var layoutId = await RegisterLayout();

		var first = Task.Run(() => registry.RegisterAsync(ProviderId, Descriptor("First name", "SERIAL-1", layoutId)));
		var second = Task.Run(()
			=> registry.RegisterAsync(ProviderId, Descriptor("Second name", "SERIAL-1", layoutId)));
		await Task.WhenAll(first, second);

		await using var context = new DatabaseContext(Log.Logger, _paths);
		var row = await context.Devices.SingleAsync(d => d.ProviderId == ProviderId);

		Assert.Multiple(() =>
		{
			Assert.That(_cache.GetAll(), Has.Count.EqualTo(1));
			Assert.That(row.StartupProfileId, Is.EqualTo(_cache.GetAll().Single().Id.ToString()));
			Assert.That(row.StarterProfilePending, Is.False);
		});
	}

	[Test]
	public async Task Concurrent_registrations_of_several_new_devices_give_each_its_own_starter_profile()
	{
		var registry = CreateRegistry(new OverlappingReads());
		var layoutId = await RegisterLayout();

		await Task.WhenAll(Enumerable.Range(1, 3)
			.Select(index => Task.Run(() => registry.RegisterAsync(ProviderId,
				Descriptor($"Deck {index}", $"SERIAL-{index}", layoutId)))));

		await using var context = new DatabaseContext(Log.Logger, _paths);
		var rows = await context.Devices.Where(d => d.ProviderId == ProviderId).ToListAsync();
		var profiles = _cache.GetAll();

		Assert.Multiple(() =>
		{
			Assert.That(profiles, Has.Count.EqualTo(3));
			Assert.That(rows.Select(row => row.StartupProfileId),
				Is.EquivalentTo(profiles.Select(profile => profile.Id.ToString())));
			Assert.That(rows.Select(row => _cache.GetById(Guid.Parse(row.StartupProfileId!))!.Name),
				Is.EquivalentTo(rows.Select(row => row.Name)));
		});
	}

	[Test]
	public async Task A_full_device_update_never_sets_the_starter_profile_decision_back()
	{
		var registry = CreateRegistry(new OverlappingReads());
		var layoutId = await RegisterLayout();
		var registration = await registry.RegisterAsync(ProviderId, Descriptor("Deck", "SERIAL-1", layoutId));
		var id = Guid.Parse(registration.DeviceId);

		await using (var stale = new DatabaseContext(Log.Logger, _paths))
		{
			var device = await stale.Devices.SingleAsync(d => d.Id == id);
			device.StarterProfilePending = true;
			device.StartupProfileId = null;
			await new DeviceRepository(stale).Update(device);
		}

		await registry.RegisterAsync(ProviderId, Descriptor("Deck", "SERIAL-1", layoutId));

		await using var context = new DatabaseContext(Log.Logger, _paths);
		var row = await context.Devices.SingleAsync(d => d.Id == id);
		Assert.Multiple(() =>
		{
			Assert.That(row.StarterProfilePending, Is.False);
			Assert.That(row.StartupProfileId, Is.Null);
			Assert.That(_cache.GetAll(), Has.Count.EqualTo(1));
		});
	}

	private async Task<string> RegisterLayout()
	{
		var registration = await _layouts.Register(ProviderId,
			new LayoutDescriptor("mk2",
				"MK.2",
				[
					new LayoutRegion
						{ Id = "keys", Kind = LayoutRegionKinds.Grid, Grid = new LayoutGrid { Rows = 3, Columns = 5 } }
				]));
		return registration.LayoutId;
	}

	private PluginDeviceRegistry CreateRegistry(OverlappingReads reads)
	{
		var mediator = new RecordingMediator();
		var secrets = new FakeSecretService();
		var services = new ServiceCollection();
		services.AddScoped(_ => new DatabaseContext(Log.Logger, _paths));
		services.AddScoped<IDeviceRepository>(provider
			=> new HoldingDeviceRepository(new DeviceRepository(provider.GetRequiredService<DatabaseContext>()),
				reads));
		services.AddSingleton<IMediator>(mediator);
		services.AddSingleton<IProfileCache>(_cache);
		services.AddSingleton<IFolderCache>(_folderCache);
		services.AddSingleton<IWidgetTypeRegistry>(new WidgetTypeRegistry(new RecordingMediator()));
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton<IAppPreferenceService>(new FakeLocalizationPreferences());
		services.AddScoped<IProfileService>(provider => new ProfileService(_cache,
			_folderCache,
			provider.GetRequiredService<IDeviceRepository>(),
			mediator,
			new WidgetSecretCloner(secrets),
			new WidgetSecretScrubber(secrets),
			new NullWidgetVariableCloner()));
		services.AddScoped<IWidgetService>(provider => new WidgetService(_folderCache,
			_cache,
			mediator,
			new WidgetSecretScrubber(secrets),
			new WidgetSecretCloner(secrets),
			new NullWidgetVariableCloner(),
			null,
			null,
			provider.GetRequiredService<IDeviceRepository>()));
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		var time = new ManualTimeProvider();
		var readiness = new StartupReadiness();
		readiness.MarkCachesReady();
		var gates = new ProviderDeviceGates();
		_starterProfiles = new ProviderDeviceStarterProfiles(scopeFactory,
			gates,
			_layouts,
			readiness,
			Serilog.Core.Logger.None);
		return new PluginDeviceRegistry(scopeFactory,
			new ProviderDevicePresenceTracker(),
			new DeviceConnectionTracker(new RecordingEventBus(),
				time,
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None)),
			new DeviceSessionGuard(),
			_layouts,
			gates,
			_starterProfiles,
			time);
	}

	private static DeviceDescriptor Descriptor(string name, string id = "SERIAL-1", string? layoutReference = null)
	{
		return new DeviceDescriptor(id,
			name,
			"Stream Deck XL",
			"Elgato",
			layoutReference,
			new DeviceCapabilities { KeyCount = 32, SupportsImages = true },
			DevicePresence.Online);
	}

	private sealed class OverlappingReads
	{
		private readonly TaskCompletionSource _secondRead =
			new(TaskCreationOptions.RunContinuationsAsynchronously);

		private int _count;

		public Task AfterRead()
		{
			switch (Interlocked.Increment(ref _count))
			{
				case 1:
					return Task.WhenAny(_secondRead.Task, Task.Delay(TimeSpan.FromSeconds(1)));
				case 2:
					_secondRead.TrySetResult();
					break;
			}

			return Task.CompletedTask;
		}
	}

	private sealed class HoldingDeviceRepository : IDeviceRepository
	{
		private readonly IDeviceRepository _inner;
		private readonly OverlappingReads _reads;

		public HoldingDeviceRepository(IDeviceRepository inner, OverlappingReads reads)
		{
			_inner = inner;
			_reads = reads;
		}

		public async Task<DeviceEntity?> GetByProviderIdentity(string providerId, string providerDeviceId)
		{
			var device = await _inner.GetByProviderIdentity(providerId, providerDeviceId);
			await _reads.AfterRead();
			return device;
		}

		public Task<DeviceEntity?> GetById(Guid id)
		{
			return _inner.GetById(id);
		}

		public Task<IReadOnlyList<DeviceEntity>> GetAll()
		{
			return _inner.GetAll();
		}

		public Task<IReadOnlyList<DeviceEntity>> GetByStartupProfileId(string profileId)
		{
			return _inner.GetByStartupProfileId(profileId);
		}

		public Task<IReadOnlyList<DeviceEntity>> GetByProviderId(string providerId)
		{
			return _inner.GetByProviderId(providerId);
		}

		public Task Create(DeviceEntity device)
		{
			return _inner.Create(device);
		}

		public Task Update(DeviceEntity device)
		{
			return _inner.Update(device);
		}

		public Task Delete(Guid id)
		{
			return _inner.Delete(id);
		}

		public Task<bool> SetSettingsButtonHidden(Guid id, bool hidden)
		{
			return _inner.SetSettingsButtonHidden(id, hidden);
		}

		public Task<IReadOnlyList<(Guid Id, DateTime? SessionsRevokedAt)>> GetSessionRevocations()
		{
			return _inner.GetSessionRevocations();
		}

		public Task<bool> ClaimStarterProfile(Guid id)
		{
			return _inner.ClaimStarterProfile(id);
		}

		public Task<bool> AssignStarterProfile(Guid id, string profileId, string? layoutSnapshot)
		{
			return _inner.AssignStarterProfile(id, profileId, layoutSnapshot);
		}

		public Task TouchLastSeen(IReadOnlyCollection<Guid> ids, DateTime seenAt)
		{
			return _inner.TouchLastSeen(ids, seenAt);
		}

		public Task<IReadOnlyList<DeviceEntity>> GetStale(DateTime lastSeenBefore)
		{
			return _inner.GetStale(lastSeenBefore);
		}

		public Task DeleteMany(IReadOnlyCollection<Guid> ids)
		{
			return _inner.DeleteMany(ids);
		}
	}
}
