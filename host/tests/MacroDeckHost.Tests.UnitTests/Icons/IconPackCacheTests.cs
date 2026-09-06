using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconPackCacheTests
{
	private IconTestHarness _harness = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task AddOrUpdatePack_PersistsManifestImmediately()
	{
		await _harness.CreatePack("Persisted");

		var reloaded = _harness.PackStore.LoadAll();
		Assert.That(reloaded, Has.Count.EqualTo(1));
		Assert.That(reloaded[0].Name, Is.EqualTo("Persisted"));
	}

	[Test]
	public async Task InitializeCache_LoadsPacksAndIconsAndBuildsIndex()
	{
		var pack = await _harness.CreatePack();
		var icon = CreateIcon(pack.Id, "logo");
		await _harness.Cache.AddIcons(pack.Id, [icon]);

		using var fresh = new IconTestHarnessSecondCache(_harness);
		await fresh.Cache.InitializeCache();

		Assert.Multiple(() =>
		{
			Assert.That(fresh.Cache.GetAllPacks(), Has.Count.EqualTo(1));
			Assert.That(fresh.Cache.GetIconById(icon.Id)?.Name, Is.EqualTo("logo"));
			Assert.That(fresh.Cache.GetIconsByPackId(pack.Id), Has.Count.EqualTo(1));
			Assert.That(fresh.Cache.GetIconCount(pack.Id), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task UpdateIcon_IsDebounced_AndFlushForcesPersistence()
	{
		var pack = await _harness.CreatePack();
		var icon = CreateIcon(pack.Id, "logo");
		await _harness.Cache.AddIcons(pack.Id, [icon]);

		icon.ProcessingState = IconProcessingState.Ready;
		await _harness.Cache.UpdateIcon(icon);

		var beforeFlush = _harness.PackStore.LoadAll().Single().Icons.Single();
		Assert.That(beforeFlush.State, Is.EqualTo(IconProcessingState.Pending));

		await _harness.Cache.FlushPendingWrites();

		var afterFlush = _harness.PackStore.LoadAll().Single().Icons.Single();
		Assert.That(afterFlush.State, Is.EqualTo(IconProcessingState.Ready));
	}

	[Test]
	public async Task RemovePack_RemovesPackIconsAndFolder()
	{
		var pack = await _harness.CreatePack();
		var icon = CreateIcon(pack.Id, "logo");
		await _harness.Cache.AddIcons(pack.Id, [icon]);

		await _harness.Cache.RemovePack(pack.Id);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetPackById(pack.Id), Is.Null);
			Assert.That(_harness.Cache.GetIconById(icon.Id), Is.Null);
			Assert.That(_harness.PackStore.LoadAll(), Is.Empty);
		});
	}

	[Test]
	public async Task GetDefaultPack_FindsTheDefault()
	{
		await _harness.CreatePack("Custom");
		var defaultPack = await _harness.CreatePack("Imported Icons", isDefault: true);

		Assert.That(_harness.Cache.GetDefaultPack()?.Id, Is.EqualTo(defaultPack.Id));
	}

	[Test]
	public async Task GetIconsByState_FiltersAcrossPacks()
	{
		var pack = await _harness.CreatePack();
		var pending = CreateIcon(pack.Id, "pending");
		var ready = CreateIcon(pack.Id, "ready");
		ready.ProcessingState = IconProcessingState.Ready;
		await _harness.Cache.AddIcons(pack.Id, [pending, ready]);

		var found = _harness.Cache.GetIconsByState(IconProcessingState.Pending);

		Assert.That(found.Select(i => i.Id), Is.EqualTo(new[] { pending.Id }));
	}

	// --- Content indexes (issue #284) ---

	[Test]
	public async Task FindBySourceContentHash_ReturnsTheIcon_AndIsScopedByPackWhenAsked()
	{
		var packA = await _harness.CreatePack("A");
		var packB = await _harness.CreatePack("B");
		var inA = await _harness.AddReadyIcon(packA.Id, "logo", [1, 2, 3]);
		var hash = SourceContentHash.Compute([1, 2, 3]);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.FindBySourceContentHash(hash)?.Id, Is.EqualTo(inA.Id));
			Assert.That(_harness.Cache.FindBySourceContentHash(hash, packA.Id)?.Id, Is.EqualTo(inA.Id));
			Assert.That(_harness.Cache.FindBySourceContentHash(hash, packB.Id), Is.Null);
		});
	}

	// The pack-scoped index is its own lookup, not a filter over the catalog-wide winner: an older icon
	// elsewhere must not hide the one that actually lives in the requested pack.
	[Test]
	public async Task FindBySourceContentHash_WithinPack_FindsTheIconEvenWhenAnOlderOneExistsElsewhere()
	{
		var older = await _harness.CreatePack("Older");
		var target = await _harness.CreatePack("Target");
		await _harness.AddReadyIcon(older.Id, "logo", [1, 2, 3]);
		var inTarget = await _harness.AddReadyIcon(target.Id, "logo", [1, 2, 3]);

		Assert.That(_harness.Cache.FindBySourceContentHash(SourceContentHash.Compute([1, 2, 3]), target.Id)?.Id,
			Is.EqualTo(inTarget.Id));
	}

	[Test]
	public async Task FindByMasterContentHash_MatchesTheStoredMasterBytes()
	{
		var pack = await _harness.CreatePack();
		var icon = await _harness.AddReadyIcon(pack.Id, "logo", [4, 5, 6], source: [9, 9]);

		Assert.That(_harness.Cache.FindByMasterContentHash(MasterContentHash.Compute([4, 5, 6]))?.Id,
			Is.EqualTo(icon.Id));
	}

	[Test]
	public async Task ContentIndex_OnlyHoldsReadyIcons()
	{
		var pack = await _harness.CreatePack();
		var icon = await _harness.AddReadyIcon(pack.Id, "logo", [1, 2, 3]);
		var hash = SourceContentHash.Compute([1, 2, 3]);
		Assert.That(_harness.Cache.FindBySourceContentHash(hash), Is.Not.Null);

		icon.ProcessingState = IconProcessingState.Failed;
		await _harness.Cache.UpdateIcon(icon);

		Assert.That(_harness.Cache.FindBySourceContentHash(hash), Is.Null);
	}

	// Icons are mutated in place and only then handed back, so the cache cannot read the previous values
	// off the entity - it has to remember what each icon contributed.
	[Test]
	public async Task ContentIndex_FollowsAnInPlaceHashChange()
	{
		var pack = await _harness.CreatePack();
		var icon = await _harness.AddReadyIcon(pack.Id, "logo", [1, 2, 3]);

		icon.SourceContentHash = SourceContentHash.Compute([7, 7]).Value;
		await _harness.Cache.UpdateIcon(icon);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.FindBySourceContentHash(SourceContentHash.Compute([1, 2, 3])), Is.Null);
			Assert.That(_harness.Cache.FindBySourceContentHash(SourceContentHash.Compute([7, 7]))?.Id,
				Is.EqualTo(icon.Id));
		});
	}

	[Test]
	public async Task RemovingAnIcon_TakesItOutOfTheIndex()
	{
		var pack = await _harness.CreatePack();
		var icon = await _harness.AddReadyIcon(pack.Id, "logo", [1, 2, 3]);

		await _harness.Cache.RemoveIcon(icon.Id);

		Assert.That(_harness.Cache.FindBySourceContentHash(SourceContentHash.Compute([1, 2, 3])), Is.Null);
	}

	// Deleting one of two identical icons must not make the survivor invisible to deduplication.
	[Test]
	public async Task RemovingTheCanonicalIcon_PromotesTheRemainingDuplicate()
	{
		var pack = await _harness.CreatePack();
		var first = await _harness.AddReadyIcon(pack.Id, "logo", [1, 2, 3]);
		var second = await _harness.AddReadyIcon(pack.Id, "copy", [1, 2, 3]);
		var hash = SourceContentHash.Compute([1, 2, 3]);
		Assert.That(_harness.Cache.FindBySourceContentHash(hash)?.Id, Is.EqualTo(first.Id), "oldest wins");

		await _harness.Cache.RemoveIcon(first.Id);

		Assert.That(_harness.Cache.FindBySourceContentHash(hash)?.Id, Is.EqualTo(second.Id));
	}

	[Test]
	public async Task RemovingAPack_TakesItsIconsOutOfTheIndex()
	{
		var pack = await _harness.CreatePack();
		await _harness.AddReadyIcon(pack.Id, "logo", [1, 2, 3]);

		await _harness.Cache.RemovePack(pack.Id);

		Assert.That(_harness.Cache.FindBySourceContentHash(SourceContentHash.Compute([1, 2, 3])), Is.Null);
	}

	[Test]
	public async Task ContentIndex_IsRebuiltFromManifestsOnStartup()
	{
		var pack = await _harness.CreatePack();
		var icon = await _harness.AddReadyIcon(pack.Id, "logo", [1, 2, 3]);
		await _harness.Cache.FlushPendingWrites();

		using var reloaded = new IconTestHarnessSecondCache(_harness);
		await reloaded.Cache.InitializeCache();

		Assert.Multiple(() =>
		{
			Assert.That(reloaded.Cache.FindBySourceContentHash(SourceContentHash.Compute([1, 2, 3]))?.Id,
				Is.EqualTo(icon.Id));
			Assert.That(reloaded.Cache.FindByMasterContentHash(MasterContentHash.Compute([1, 2, 3]))?.Id,
				Is.EqualTo(icon.Id));
		});
	}

	[Test]
	public async Task LegacyChecksums_AreNotTreatedAsSourceHashes()
	{
		var pack = await _harness.CreatePack();
		var legacy = "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad";
		_harness.PackStore.Save(new Application.Persistence.Icons.IconPackManifest
		{
			Id = pack.Id,
			Name = pack.Name,
			CreatedAt = pack.CreatedAt,
			Icons =
			[
				new Application.Persistence.Icons.IconManifestEntry
				{
					Id = Guid.CreateVersion7(),
					Name = "legacy",
					Checksum = legacy,
					State = IconProcessingState.Ready,
					CreatedAt = DateTime.UtcNow
				}
			]
		});

		using var reloaded = new IconTestHarnessSecondCache(_harness);
		await reloaded.Cache.InitializeCache();

		var icon = reloaded.Cache.GetIconsByPackId(pack.Id).Single();
		Assert.Multiple(() =>
		{
			Assert.That(icon.DeclaredSourceContentHash, Is.EqualTo("sha256:" + legacy));
			Assert.That(icon.SourceContentHash, Is.Null);
			Assert.That(reloaded.Cache.FindBySourceContentHash(SourceContentHash.FromComputed("sha256:" + legacy)),
				Is.Null);
		});
	}

	private static IconEntity CreateIcon(Guid packId, string name)
		=> new()
		{
			Id = Guid.CreateVersion7(),
			PackId = packId,
			Name = name,
			CreatedAt = DateTime.UtcNow
		};

	private sealed class IconTestHarnessSecondCache : IDisposable
	{
		public Infrastructure.Caching.IconPackCache Cache { get; }

		public IconTestHarnessSecondCache(IconTestHarness harness)
		{
			Cache = new Infrastructure.Caching.IconPackCache(harness.PackStore, harness.Storage, harness.Logger);
		}

		public void Dispose()
		{
			Cache.Dispose();
		}
	}
}
