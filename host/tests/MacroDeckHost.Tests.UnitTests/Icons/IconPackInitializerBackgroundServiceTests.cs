using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Icons.Included;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.IconPacks;
using MacroDeckHost.Infrastructure.Icons;
using MacroDeckHost.Infrastructure.Store;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
internal sealed class IconPackInitializerBackgroundServiceTests
{
	private IconTestHarness _harness = null!;
	private JsonStoreInstallationStore _installations = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_harness.Paths.EnsureDirectoriesExist();
		Directory.CreateDirectory(_harness.Paths.StoreDirectory);
		_installations = new JsonStoreInstallationStore(_harness.Paths, _harness.Logger);
	}

	[TearDown]
	public void TearDown() => _harness.Dispose();

	// User decision 3: already-stale records self-heal. IconPackInitializerBackgroundService is the
	// only production caller of PruneOrphanedIconPackRecords(), so this pins the observable behaviour
	// through that call site rather than by invoking the reconciler directly (scenarios 6/7 already
	// cover the reconciler itself).
	[Test]
	public async Task Startup_prunes_an_orphaned_installation_record_and_leaves_a_live_one_untouched()
	{
		var livePack = await _harness.CreatePack("Still Here");
		livePack.SourceType = IconPackSourceType.ExtensionStore;
		livePack.SourceId = "com.acme.still-here";
		await _harness.Cache.AddOrUpdatePack(livePack);

		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.test/",
			Kind = StoreExtensionKind.IconPack,
			PackageId = "com.acme.orphan",
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow,
			TargetIds = [Guid.CreateVersion7()]
		});
		_installations.Save(new StoreInstallationRecord
		{
			Origin = "https://registry.test/",
			Kind = StoreExtensionKind.IconPack,
			PackageId = "com.acme.still-here",
			Version = "1.0.0",
			InstalledAt = DateTimeOffset.UtcNow,
			TargetIds = [livePack.Id]
		});

		var reconciler = new StoreInstallationReconciler(_installations, _harness.Cache);
		var service = new IconPackInitializerBackgroundService(new StartedHostLifetime(),
			_harness.Cache,
			reconciler,
			new ThrowingIncludedIconPackSync(),
			_harness.Paths,
			new StartupReadiness(),
			_harness.Logger);

		await service.StartAsync(CancellationToken.None);
		if (service.ExecuteTask is { } executeTask)
		{
			await executeTask;
		}

		await service.StopAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_installations.Find(StoreExtensionKind.IconPack, "com.acme.orphan"), Is.Null);
			Assert.That(_installations.Find(StoreExtensionKind.IconPack, "com.acme.still-here"), Is.Not.Null);
		});
	}

	[Test]
	public async Task Icon_packs_become_ready_when_the_included_pack_cannot_be_synced_at_all()
	{
		var readiness = new StartupReadiness();

		await Run(new ThrowingIncludedIconPackSync(), readiness);

		Assert.That(readiness.WhenIconPacksReady.IsCompletedSuccessfully, Is.True);
	}

	[Test]
	public async Task Icon_packs_become_ready_with_the_rest_of_the_included_pack_when_one_icon_fails()
	{
		var readiness = new StartupReadiness();
		var services = new ServiceCollection().AddSingleton<IMediator>(_harness.Mediator).BuildServiceProvider();
		var processor = new IncludedIconPackSyncTests.CountingProcessor(new ImageSharpIconProcessor(_harness.Logger))
		{
			FailFor = IncludedIconPack.Gpu
		};
		var assets = new EmbeddedIncludedIconAssets().Load()
			.Where(asset => asset.Name is IncludedIconPack.Cpu or IncludedIconPack.Gpu or IncludedIconPack.Battery)
			.ToList();
		var sync = new IncludedIconPackSync(new IncludedIconPackSyncTests.FakeAssets(assets),
			_harness.Cache,
			_harness.Storage,
			processor,
			new IconUsageScannerStub(),
			services.GetRequiredService<IServiceScopeFactory>(),
			readiness,
			TimeProvider.System,
			_harness.Logger);

		await Run(sync, readiness);

		Assert.Multiple(() =>
		{
			Assert.That(readiness.WhenIconPacksReady.IsCompletedSuccessfully, Is.True);
			Assert.That(_harness.Cache.GetIconById(IncludedIconPack.IconId(IncludedIconPack.Cpu)), Is.Not.Null);
			Assert.That(_harness.Cache.GetIconById(IncludedIconPack.IconId(IncludedIconPack.Battery)), Is.Not.Null);
			Assert.That(_harness.Cache.GetIconById(IncludedIconPack.IconId(IncludedIconPack.Gpu)), Is.Null);
		});
	}

	private async Task Run(IIncludedIconPackSync sync, StartupReadiness readiness)
	{
		var service = new IconPackInitializerBackgroundService(new StartedHostLifetime(),
			_harness.Cache,
			new StoreInstallationReconciler(_installations, _harness.Cache),
			sync,
			_harness.Paths,
			readiness,
			_harness.Logger);

		await service.StartAsync(CancellationToken.None);
		if (service.ExecuteTask is { } executeTask)
		{
			await executeTask;
		}

		await service.StopAsync(CancellationToken.None);
	}

	private sealed class ThrowingIncludedIconPackSync : IIncludedIconPackSync
	{
		public Task SyncAsync(CancellationToken cancellationToken) => throw new InvalidOperationException("broken");
	}

	private sealed class IconUsageScannerStub : IIconUsageScanner
	{
		public IReadOnlySet<Guid> FindReferencedIconIds() => new HashSet<Guid>();
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(canceled: true);
		public CancellationToken ApplicationStopping { get; } = CancellationToken.None;
		public CancellationToken ApplicationStopped { get; } = CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
