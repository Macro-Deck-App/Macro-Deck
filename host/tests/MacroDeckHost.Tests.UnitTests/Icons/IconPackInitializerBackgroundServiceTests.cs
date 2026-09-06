using MacroDeckHost.Application.Store.Installation;
using MacroDeckHost.Application.Store.Model;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Store;
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
			_harness.Paths,
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
