using MacroDeck.Sdk.Devices;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Layouts;
using MacroDeckHost.Application.Paths;
using MacroDeckHost.Application.Persistence.Repositories;
using MacroDeckHost.Domain.Entities;
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

	[SetUp]
	public void SetUp()
	{
		_dataDir = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		_previousDataDir = Environment.GetEnvironmentVariable("MACRODECK_DATA_DIR");
		Environment.SetEnvironmentVariable("MACRODECK_DATA_DIR", _dataDir);

		_paths = new MacroDeckPaths();
		_paths.EnsureDirectoriesExist();
		DatabaseMigrationHelper.MigrateDatabase(_paths);
	}

	[TearDown]
	public void TearDown()
	{
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

	private PluginDeviceRegistry CreateRegistry(OverlappingReads reads)
	{
		var services = new ServiceCollection();
		services.AddScoped(_ => new DatabaseContext(Log.Logger, _paths));
		services.AddScoped<IDeviceRepository>(provider
			=> new HoldingDeviceRepository(new DeviceRepository(provider.GetRequiredService<DatabaseContext>()),
				reads));
		services.AddSingleton<IMediator>(new RecordingMediator());
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		var time = new ManualTimeProvider();
		return new PluginDeviceRegistry(scopeFactory,
			new ProviderDevicePresenceTracker(),
			new DeviceConnectionTracker(new RecordingEventBus(),
				time,
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None)),
			new DeviceSessionGuard(),
			new LayoutRegistry(new RecordingMediator()),
			time);
	}

	private static DeviceDescriptor Descriptor(string name)
	{
		return new DeviceDescriptor("SERIAL-1",
			name,
			"Stream Deck XL",
			"Elgato",
			null,
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
