using System.IO.Compression;
using System.Text;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Infrastructure.Icons;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconProcessingBackgroundServiceTests
{
	private static readonly int[] _expectedGoodSizes = [128, 256];
	private static readonly string[] _expectedFallbackNames = ["NoInfo", "Root"];

	private IconTestHarness _harness = null!;
	private IconProcessingBackgroundService _service = null!;
	private ServiceProvider _serviceProvider = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_serviceProvider = new ServiceCollection()
			.AddSingleton<IMediator>(_harness.Mediator)
			.BuildServiceProvider();
		_service = new IconProcessingBackgroundService(new StartedHostLifetime(),
			_harness.Cache,
			_harness.Storage,
			new ImageSharpIconProcessor(_harness.Logger),
			_harness.BatchTracker,
			_harness.BatchFinalizer,
			_harness.ProcessingChannel,
			_harness.CancellationRegistry,
			_harness.Coalescer,
			_serviceProvider.GetRequiredService<IServiceScopeFactory>(),
			_harness.Logger);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _service.StopAsync(CancellationToken.None);
		_service.Dispose();
		await _serviceProvider.DisposeAsync();
		_harness.Dispose();
	}

	[Test]
	public async Task Import_ProcessesFilesInBackground_OneFailureNeverCancelsTheBatch()
	{
		var pack = await _harness.CreatePack();
		var importService = _harness.CreateImportService();

		var result = await importService.Import(pack.Id,
			"drop",
			Files(("good.png", CreatePng(300, 200)), ("broken.png", Encoding.UTF8.GetBytes("nope"))),
			CancellationToken.None);
		Assert.That(result.Success, Is.True);
		var batchId = result.Data!.Id;

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() =>
			_harness.Cache.GetIconsByPackId(pack.Id).All(IsTerminal) &&
			_harness.BatchTracker.Get(batchId) is null &&
			_harness.Storage.EnumerateStagedBatchIds().Count == 0);

		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		var good = icons.Single(i => i.Name == "good");
		var broken = icons.Single(i => i.Name == "broken");
		Assert.Multiple(() =>
		{
			Assert.That(good.ProcessingState, Is.EqualTo(IconProcessingState.Ready));
			Assert.That(good.AvailableSizes, Is.EqualTo(_expectedGoodSizes));
			Assert.That(good.SourceContentHash, Does.StartWith("sha256:"));
			Assert.That(good.MasterContentHash, Does.StartWith("sha256:"));
			Assert.That(broken.ProcessingState, Is.EqualTo(IconProcessingState.Failed));
			Assert.That(broken.ProcessingError, Is.Not.Null);
		});

		await using (var master = _harness.Storage.OpenVariant(pack.Id, good.Id, IconVariants.Master))
		{
			Assert.That(master, Is.Not.Null);
		}

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Storage.EnumerateStagedBatchIds(), Is.Empty);
			Assert.That(_harness.PackStore.LoadAll().Single().Icons.Single(i => i.Id == good.Id).State,
				Is.EqualTo(IconProcessingState.Ready));
		});

		var progress = _harness.Mediator.Published.OfType<IconImportProgressNotification>().ToList();
		Assert.That(progress.Any(p => p.Batch.State == IconImportBatchState.CompletedWithErrors),
			Is.True,
			"a terminal progress event must be published");
	}

	[Test]
	public async Task CancelBatch_RemovesUnfinishedIcons_KeepsReadyIcons_AndPublishesCancelledTerminalState()
	{
		var pack = await _harness.CreatePack();
		var importService = _harness.CreateImportService();

		var result = await importService.Import(pack.Id,
			"drop",
			Files(("a.png", CreatePng(32, 32, tint: 1)),
				("b.png", CreatePng(32, 32, tint: 2)),
				("c.png", CreatePng(32, 32, tint: 3))),
			CancellationToken.None);
		Assert.That(result.Success, Is.True);
		var batchId = result.Data!.Id;

		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		var alreadyReady = icons[0];
		alreadyReady.ProcessingState = IconProcessingState.Ready;
		await _harness.Cache.UpdateIcon(alreadyReady);
		_harness.BatchTracker.IncrementProcessed(batchId);

		var cancelled = await importService.CancelBatch(batchId, CancellationToken.None);
		Assert.That(cancelled, Is.True);

		var remaining = _harness.Cache.GetIconsByBatchId(batchId);
		Assert.Multiple(() =>
		{
			Assert.That(remaining.Select(i => i.Id),
				Is.EquivalentTo(new[] { alreadyReady.Id }),
				"only the already-Ready icon may survive a cancel");
			Assert.That(_harness.BatchTracker.Get(batchId), Is.Null, "the tracker must no longer know the batch");
			Assert.That(_harness.Storage.EnumerateStagedBatchIds(),
				Does.Not.Contain(batchId),
				"staging must be swept");
		});

		var progress = _harness.Mediator.Published.OfType<IconImportProgressNotification>().ToList();
		Assert.That(progress.Any(p => p.Batch.Id == batchId && p.Batch.State == IconImportBatchState.Cancelled),
			Is.True,
			"a terminal Cancelled progress event must be published");

		await _service.StartAsync(CancellationToken.None);
		await Task.Delay(200);
		Assert.That(_harness.Cache.GetIconsByBatchId(batchId).Select(i => i.Id),
			Is.EquivalentTo(new[] { alreadyReady.Id }));
	}

	[Test]
	public async Task CancelBatch_UnknownOrAlreadyFinishedBatch_ReturnsFalse_AndPublishesNothing()
	{
		var importService = _harness.CreateImportService();

		var cancelled = await importService.CancelBatch(Guid.NewGuid(), CancellationToken.None);

		Assert.That(cancelled, Is.False);
		Assert.That(_harness.Mediator.Published, Is.Empty);
	}

	[Test]
	public async Task ZipImport_ExtractsRecursively_AndCompletes()
	{
		var pack = await _harness.CreatePack();
		var importService = _harness.CreateImportService();
		var zip = CreateZip(("a.png", CreatePng(64, 64)),
			("nested/b.png", CreatePng(32, 32)),
			("skip.txt", [1, 2]));

		var result = await importService.Import(pack.Id,
			"icons.zip",
			Files(("icons.zip", zip)),
			CancellationToken.None);
		var batchId = result.Data!.Id;

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() =>
		{
			var current = _harness.Cache.GetIconsByPackId(pack.Id);
			return current.Count == 2 &&
				current.All(i => i.ProcessingState == IconProcessingState.Ready) &&
				_harness.BatchTracker.Get(batchId) is null;
		});

		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		Assert.That(icons.Select(i => i.OriginalFileName), Does.Contain("icons.zip/nested/b.png"));
	}

	[Test]
	public async Task StreamDeckIconPack_WithoutExplicitDestination_CreatesItsOwnPack()
	{
		await _harness.CreatePack("Imported Icons", isDefault: true);
		var importService = _harness.CreateImportService();
		var package = CreateZip(("manifest.json",
				Encoding.UTF8.GetBytes("""{"Name":"Cool Pack","Author":"Elgato","Version":"2.1"}""")),
			("icons/star.png", CreatePng(64, 64)));

		var result = await importService.Import(null,
			"cool.streamDeckIconPack",
			Files(("cool.streamDeckIconPack", package)),
			CancellationToken.None);
		Assert.That(result.Success, Is.True);

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() =>
			_harness.Cache.GetAllPacks().Any(p => p.Name == "Cool Pack") &&
			_harness.Cache.GetIconsByPackId(_harness.Cache.GetAllPacks().Single(p => p.Name == "Cool Pack").Id)
				.All(i => i.ProcessingState == IconProcessingState.Ready) &&
			_harness.Cache.GetIconsByPackId(_harness.Cache.GetAllPacks().Single(p => p.Name == "Cool Pack").Id).Count ==
			1);

		var created = _harness.Cache.GetAllPacks().SingleOrDefault(p => p.Name == "Cool Pack");
		Assert.That(created, Is.Not.Null);
		Assert.Multiple(() =>
		{
			Assert.That(created!.Author, Is.EqualTo("Elgato"));
			Assert.That(created.Version, Is.EqualTo("2.1"));
			Assert.That(created.SourceType, Is.EqualTo(IconPackSourceType.StreamDeckImport));
			Assert.That(_harness.Cache.GetIconsByPackId(created.Id), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task ImportPacks_Zip_CreatesPackNamedAfterArchive()
	{
		await _harness.CreatePack("Imported Icons", isDefault: true);
		var importService = _harness.CreateImportService();
		var zip = CreateZip(("a.png", CreatePng(32, 32, tint: 1)), ("nested/b.png", CreatePng(32, 32, tint: 2)));

		var result = await importService.ImportPacks(Files(("Cool Icons.zip", zip)), CancellationToken.None);
		Assert.That(result.Success, Is.True);

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() => _harness.Cache.GetAllPacks().Any(p => p.Name == "Cool Icons") &&
			_harness.Cache.GetIconsByPackId(_harness.Cache.GetAllPacks().Single(p => p.Name == "Cool Icons").Id)
				.Count(i => i.ProcessingState == IconProcessingState.Ready) ==
			2);

		var created = _harness.Cache.GetAllPacks().Single(p => p.Name == "Cool Icons");
		Assert.Multiple(() =>
		{
			Assert.That(created.SourceType, Is.EqualTo(IconPackSourceType.User));
			Assert.That(created.SourceId, Does.StartWith("import:"));
			Assert.That(created.IsDefault, Is.False);
		});
	}

	[Test]
	public async Task ImportPacks_Tpi_CreatesOnePackPerInfoFolder_AndFallbackForOtherEntries()
	{
		await _harness.CreatePack("Imported Icons", isDefault: true);
		var importService = _harness.CreateImportService();
		var tpi = CreateZip(("GICO-Misc/info.txt",
				Encoding.UTF8.GetBytes("name=BashTux-GICO-Misc\nauthor=BashTux\nlink=https://example.com")),
			("GICO-Misc/Back.png", CreatePng(32, 32, tint: 1)),
			("GICO-Games/info.txt", Encoding.UTF8.GetBytes("name=BashTux-GICO-Games\nauthor=BashTux")),
			("GICO-Games/Steam.png", CreatePng(32, 32, tint: 2)),
			("Loose/NoInfo.png", CreatePng(32, 32, tint: 3)),
			("Root.png", CreatePng(32, 32, tint: 4)));

		var result = await importService.ImportPacks(Files(("BashTux.tpi", tpi)), CancellationToken.None);
		Assert.That(result.Success, Is.True);

		await _service.StartAsync(CancellationToken.None);
		// The icon count has to be waited for as well as the pack count: All() over an empty list is
		// vacuously true, so waiting on the states alone lets the wait fall through the moment the packs
		// exist but before a single icon has been attached to them.
		await WaitUntil(() => _harness.Cache.GetAllPacks().Count == 4 &&
			_harness.Cache.GetIconsByBatchId(result.Data!.Batch!.Id) is { Count: 4 } icons &&
			icons.All(i => i.ProcessingState == IconProcessingState.Ready));

		var packs = _harness.Cache.GetAllPacks();
		var misc = packs.Single(p => p.Name == "BashTux-GICO-Misc");
		var games = packs.Single(p => p.Name == "BashTux-GICO-Games");
		var fallback = packs.Single(p => p.Name == "BashTux");
		Assert.Multiple(() =>
		{
			Assert.That(misc.Author, Is.EqualTo("BashTux"));
			Assert.That(misc.Description, Is.EqualTo("https://example.com"));
			Assert.That(misc.SourceType, Is.EqualTo(IconPackSourceType.TouchPortalImport));
			Assert.That(games.Author, Is.EqualTo("BashTux"));
			Assert.That(_harness.Cache.GetIconsByPackId(misc.Id).Single().Name, Is.EqualTo("Back"));
			Assert.That(_harness.Cache.GetIconsByPackId(games.Id).Single().Name, Is.EqualTo("Steam"));
			Assert.That(_harness.Cache.GetIconsByPackId(fallback.Id).Select(i => i.Name),
				Is.EquivalentTo(_expectedFallbackNames));
		});
	}

	[Test]
	public async Task ImportPacks_FlatTpi_CreatesSinglePackNamedAfterFile()
	{
		await _harness.CreatePack("Imported Icons", isDefault: true);
		var importService = _harness.CreateImportService();
		var tpi = CreateZip(("One.png", CreatePng(32, 32, tint: 1)), ("Two.png", CreatePng(32, 32, tint: 2)));

		var result = await importService.ImportPacks(Files(("Flat Pack.tpi", tpi)), CancellationToken.None);
		Assert.That(result.Success, Is.True);

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() =>
			_harness.Cache.GetAllPacks().Any(p => p.Name == "Flat Pack") &&
			_harness.Cache.GetIconsByPackId(_harness.Cache.GetAllPacks().Single(p => p.Name == "Flat Pack").Id).Count ==
			2);

		Assert.That(_harness.Cache.GetAllPacks(), Has.Count.EqualTo(2), "default pack + one imported pack");
	}

	[Test]
	public async Task Tpi_WithExplicitDestination_ImportsIntoTargetAndIgnoresInfoTxt()
	{
		var target = await _harness.CreatePack("Target");
		var importService = _harness.CreateImportService();
		var tpi = CreateZip(("GICO-Misc/info.txt", Encoding.UTF8.GetBytes("name=Ignored\nauthor=Nobody")),
			("GICO-Misc/Back.png", CreatePng(32, 32)));

		var result = await importService.Import(target.Id,
			"BashTux.tpi",
			Files(("BashTux.tpi", tpi)),
			CancellationToken.None);
		Assert.That(result.Success, Is.True);

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() => _harness.Cache.GetIconsByPackId(target.Id)
				.Count(i => i.ProcessingState == IconProcessingState.Ready) ==
			1);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetAllPacks(), Has.Count.EqualTo(1), "no pack may be created");
			Assert.That(_harness.Cache.GetIconsByPackId(target.Id).Single().Name, Is.EqualTo("Back"));
		});
	}

	[Test]
	public async Task ImportPacks_StreamDeckIconPack_UsesManifestMetadata()
	{
		await _harness.CreatePack("Imported Icons", isDefault: true);
		var importService = _harness.CreateImportService();
		var package = CreateZip(("manifest.json",
				Encoding.UTF8.GetBytes("""{"Name":"SD Pack","Author":"Elgato","Version":"3.0"}""")),
			("icons/star.png", CreatePng(32, 32)));

		var result = await importService.ImportPacks(Files(("sd.streamDeckIconPack", package)),
			CancellationToken.None);
		Assert.That(result.Success, Is.True);

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() =>
			_harness.Cache.GetAllPacks().Any(p => p.Name == "SD Pack") &&
			_harness.Cache.GetIconsByPackId(_harness.Cache.GetAllPacks().Single(p => p.Name == "SD Pack").Id).Count ==
			1);

		var created = _harness.Cache.GetAllPacks().Single(p => p.Name == "SD Pack");
		Assert.Multiple(() =>
		{
			Assert.That(created.Author, Is.EqualTo("Elgato"));
			Assert.That(created.Version, Is.EqualTo("3.0"));
			Assert.That(created.SourceType, Is.EqualTo(IconPackSourceType.StreamDeckImport));
		});
	}

	[Test]
	public async Task ImportPacks_ReExtractionAfterCrash_ReusesTheAlreadyCreatedPack()
	{
		var defaultPack = await _harness.CreatePack("Imported Icons", isDefault: true);
		var batch = new IconImportBatchEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = defaultPack.Id,
			State = IconImportBatchState.Discovering,
			Mode = IconImportMode.NewPacks,
			CreatedAt = DateTime.UtcNow
		};
		_harness.BatchTracker.Register(batch);
		var tpi = CreateZip(("Folder/info.txt", Encoding.UTF8.GetBytes("name=Recovered Pack")),
			("Folder/a.png", CreatePng(32, 32, tint: 1)),
			("Folder/b.png", CreatePng(32, 32, tint: 2)));
		await _harness.Storage.StageArchive(batch.Id,
			"recover.tpi",
			new MemoryStream(tpi),
			CancellationToken.None);

		var existing = new IconPackEntity
		{
			Id = Guid.CreateVersion7(),
			Name = "Recovered Pack",
			SourceType = IconPackSourceType.TouchPortalImport,
			SourceId = $"import:{batch.Id:N}:recover.tpi:Folder",
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddOrUpdatePack(existing);
		batch.PackId = existing.Id;
		_harness.BatchTracker.Persist(batch);

		_harness.ProcessingChannel.Enqueue(new ExtractBatchWorkItem(batch.Id));
		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() => _harness.Cache.GetIconsByPackId(existing.Id)
				.Count(i => i.ProcessingState == IconProcessingState.Ready) ==
			2);

		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetAllPacks().Count(p => p.Name == "Recovered Pack"),
				Is.EqualTo(1),
				"re-extraction must reuse the pack instead of duplicating it");
			Assert.That(_harness.Cache.GetIconsByPackId(defaultPack.Id), Is.Empty);
		});
	}

	[Test]
	public async Task Recovery_ReprocessesPendingIconWithStagedOriginal()
	{
		var pack = await _harness.CreatePack();
		var (batch, icon) = await ArrangeInterruptedImport(pack,
			IconProcessingState.Processing,
			stageOriginal: true);
		Assert.That(batch.State, Is.EqualTo(IconImportBatchState.Processing));

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() =>
			_harness.Cache.GetIconById(icon.Id)!.ProcessingState == IconProcessingState.Ready);
	}

	[Test]
	public async Task Recovery_PendingIconWithoutStagedOriginal_IsMarkedFailed()
	{
		var pack = await _harness.CreatePack();
		var (batch, icon) = await ArrangeInterruptedImport(pack,
			IconProcessingState.Pending,
			stageOriginal: false);
		Assert.That(batch.State, Is.EqualTo(IconImportBatchState.Processing));

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() =>
			_harness.Cache.GetIconById(icon.Id)!.ProcessingState == IconProcessingState.Failed);

		Assert.That(_harness.Cache.GetIconById(icon.Id)!.ProcessingError, Is.Not.Null);
	}

	[Test]
	public async Task Recovery_OrphanedPendingIcon_WithoutBatchRecord_IsMarkedFailed()
	{
		var pack = await _harness.CreatePack();
		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			Name = "orphan",
			ProcessingState = IconProcessingState.Processing,
			ImportBatchId = Guid.NewGuid(),
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(pack.Id, [icon]);

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() =>
			_harness.Cache.GetIconById(icon.Id)!.ProcessingState == IconProcessingState.Failed);
	}

	[Test]
	public async Task Recovery_OrphanedStagingDirectory_IsSweptAway()
	{
		var orphanBatchId = Guid.NewGuid();
		await _harness.Storage.StageOriginal(orphanBatchId,
			Guid.NewGuid(),
			"x.png",
			new MemoryStream([1]),
			CancellationToken.None);

		await _service.StartAsync(CancellationToken.None);
		await WaitUntil(() => !_harness.Storage.EnumerateStagedBatchIds().Contains(orphanBatchId));
	}

	private async Task<(IconImportBatchEntity Batch, IconEntity Icon)> ArrangeInterruptedImport(
		IconPackEntity pack,
		IconProcessingState state,
		bool stageOriginal)
	{
		var batch = new IconImportBatchEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			State = IconImportBatchState.Processing,
			Total = 1,
			CreatedAt = DateTime.UtcNow
		};
		_harness.BatchTracker.Register(batch);
		_harness.BatchTracker.Persist(batch);

		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			Name = "interrupted",
			OriginalFileName = "interrupted.png",
			ProcessingState = state,
			ImportBatchId = batch.Id,
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(pack.Id, [icon]);
		await _harness.Cache.FlushPendingWrites();

		if (stageOriginal)
		{
			await _harness.Storage.StageOriginal(batch.Id,
				icon.Id,
				"interrupted.png",
				new MemoryStream(CreatePng(48, 48)),
				CancellationToken.None);
		}

		_harness.BatchTracker.TryFinish(batch.Id);
		_harness.BatchTracker.Persist(batch);
		return (batch, icon);
	}

	private static bool IsTerminal(IconEntity icon)
		=> icon.ProcessingState is IconProcessingState.Ready or IconProcessingState.Failed;

	private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 15000)
	{
		var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("Timed out waiting for the background pipeline");
			}

			await Task.Delay(50);
		}
	}

	private static async IAsyncEnumerable<IconImportFile> Files(params (string Name, byte[] Content)[] files)
	{
		foreach (var (name, content) in files)
		{
			yield return new IconImportFile(name, new MemoryStream(content));
		}

		await Task.CompletedTask;
	}

	private static byte[] CreatePng(int width, int height, byte tint = 0)
	{
		using var image = new Image<Rgba32>(width, height, new Rgba32(tint, 128, 255));
		using var stream = new MemoryStream();
		image.SaveAsPng(stream);
		return stream.ToArray();
	}

	private static byte[] CreateZip(params (string Name, byte[] Content)[] entries)
	{
		using var stream = new MemoryStream();
		using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
		{
			foreach (var (name, content) in entries)
			{
				using var entryStream = zip.CreateEntry(name).Open();
				entryStream.Write(content);
			}
		}

		return stream.ToArray();
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
