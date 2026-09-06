using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.BackgroundServices;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconMasterHashBackfillBackgroundServiceTests
{
	private IconTestHarness _harness = null!;

	[SetUp]
	public void SetUp() => _harness = new IconTestHarness();

	[TearDown]
	public void TearDown() => _harness.Dispose();

	[Test]
	public async Task Backfill_HashesLegacyMastersAndIndexesThem()
	{
		var pack = await _harness.CreatePack();
		var legacy = await AddLegacyIcon(pack.Id, "legacy", [1, 2, 3]);

		await RunBackfill();

		Assert.Multiple(() =>
		{
			Assert.That(legacy.MasterContentHash, Is.EqualTo(MasterContentHash.Compute([1, 2, 3]).Value));
			Assert.That(_harness.Cache.FindByMasterContentHash(MasterContentHash.Compute([1, 2, 3]))?.Id,
				Is.EqualTo(legacy.Id));
			Assert.That(legacy.SourceContentHash, Is.Null);
		});
	}

	// An icon with no master on disk cannot be hashed and must simply be left behind rather than, say,
	// recorded with the hash of nothing. It is seeded next to one the pass does reach, so the assertion
	// only holds once the service has actually run.
	[Test]
	public async Task Backfill_LeavesAnIconWhoseMasterIsMissing_Alone()
	{
		var pack = await _harness.CreatePack();
		var orphan = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			Name = "orphan",
			ProcessingState = IconProcessingState.Ready,
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(pack.Id, [orphan]);
		var reachable = await AddLegacyIcon(pack.Id, "reachable", [1, 2, 3]);

		await RunBackfill(until: () => reachable.MasterContentHash is not null);

		Assert.Multiple(() =>
		{
			Assert.That(orphan.MasterContentHash, Is.Null);
			Assert.That(_harness.Cache.FindByMasterContentHash(MasterContentHash.Compute([1, 2, 3]))?.Id,
				Is.EqualTo(reachable.Id),
				"the pass ran and did not stop at the icon it could not hash");
		});
	}

	// An icon that already has a hash must keep the one it has rather than be re-derived, which is what
	// would happen if the pass ignored the existing value: here the stored bytes deliberately disagree
	// with the recorded hash, so a re-hash would overwrite it.
	[Test]
	public async Task Backfill_LeavesAnExistingHashUntouched()
	{
		var pack = await _harness.CreatePack();
		var current = await _harness.AddReadyIcon(pack.Id, "current", [4, 5]);
		var recorded = current.MasterContentHash;
		await _harness.Storage.WriteVariant(pack.Id,
			current.Id,
			IconVariants.Master,
			new byte[] { 6, 7 },
			CancellationToken.None);
		var legacy = await AddLegacyIcon(pack.Id, "legacy", [1, 2, 3]);

		await RunBackfill(until: () => legacy.MasterContentHash is not null);

		Assert.That(current.MasterContentHash, Is.EqualTo(recorded));
	}

	private async Task RunBackfill(Func<bool>? until = null)
	{
		until ??= () => _harness.Cache.GetIconsMissingMasterContentHash().Count == 0;
		var service = new IconMasterHashBackfillBackgroundService(new StartedHostLifetime(),
			_harness.Cache,
			_harness.Storage,
			_harness.Logger);
		await service.StartAsync(CancellationToken.None);
		try
		{
			var deadline = DateTime.UtcNow.AddSeconds(10);
			while (!until() && DateTime.UtcNow < deadline)
			{
				await Task.Delay(20);
			}

			Assert.That(until(), Is.True, "the backfill pass did not complete");
		}
		finally
		{
			await service.StopAsync(CancellationToken.None);
		}
	}

	private async Task<IconEntity> AddLegacyIcon(Guid packId, string name, byte[] master)
	{
		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = packId,
			Name = name,
			DeclaredSourceContentHash = ContentHash.Compute("whatever the old checksum was"u8),
			ProcessingState = IconProcessingState.Ready,
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(packId, [icon]);
		await _harness.Storage.WriteVariant(packId, icon.Id, IconVariants.Master, master, CancellationToken.None);
		return icon;
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
