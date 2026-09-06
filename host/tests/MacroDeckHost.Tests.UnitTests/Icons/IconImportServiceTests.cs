using System.IO.Compression;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Icons;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconImportServiceTests
{
	private IconTestHarness _harness = null!;
	private FakeAppIconExtractor _extractor = null!;
	private IconImportService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_extractor = new FakeAppIconExtractor();
		_service = _harness.CreateImportService(_extractor);
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task Import_WithoutPackId_TargetsDefaultPack()
	{
		var defaultPack = await _harness.CreatePack("Imported Icons", isDefault: true);

		var result = await _service.Import(null, null, Files(("logo.png", [1, 2])), CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.PackId, Is.EqualTo(defaultPack.Id));
			Assert.That(result.Data!.ExplicitDestination, Is.False);
			Assert.That(_harness.Cache.GetIconsByPackId(defaultPack.Id), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Import_NoDefaultPack_FailsWithPackNotFound()
	{
		var result = await _service.Import(null, null, Files(("logo.png", [1])), CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(IconError.PackNotFound));
	}

	[Test]
	public async Task Import_IntoReadOnlyPack_IsRejected()
	{
		var pack = await _harness.CreatePack(isReadOnly: true);

		var result = await _service.Import(pack.Id, null, Files(("logo.png", [1])), CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(IconError.PackReadOnly));
	}

	[Test]
	public async Task Import_StagesImagesCreatesPendingIconsAndEnqueuesWork()
	{
		var pack = await _harness.CreatePack();

		var result = await _service.Import(pack.Id,
			"drop",
			Files(("a.png", [1]), ("sub/b.svg", [2])),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		Assert.Multiple(() =>
		{
			Assert.That(icons, Has.Count.EqualTo(2));
			Assert.That(icons.All(i => i.ProcessingState == IconProcessingState.Pending), Is.True);
			Assert.That(icons.Select(i => i.OriginalFileName), Does.Contain("sub/b.svg"));
			Assert.That(result.Data!.State, Is.EqualTo(IconImportBatchState.Processing));
			Assert.That(result.Data!.Total, Is.EqualTo(2));
		});

		var workItems = DrainChannel();
		Assert.That(workItems.OfType<ProcessIconWorkItem>().Count(), Is.EqualTo(2));

		foreach (var icon in icons)
		{
			await using var staged = _harness.Storage.OpenStagedOriginal(result.Data!.Id, icon.Id);
			Assert.That(staged, Is.Not.Null, $"original of {icon.Name} must be staged");
		}
	}

	[Test]
	public async Task Import_UnsupportedFilesAreSkipped()
	{
		var pack = await _harness.CreatePack();

		var result = await _service.Import(pack.Id,
			null,
			Files(("notes.txt", [1]), ("a.png", [2])),
			CancellationToken.None);

		Assert.That(result.Data!.Total, Is.EqualTo(1));
	}

	[Test]
	public async Task Import_LottieFilesAreStagedAsIcons()
	{
		var pack = await _harness.CreatePack();

		var result = await _service.Import(pack.Id,
			null,
			Files(("spinner.lottie", [1]), ("loader.json", [2])),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Total, Is.EqualTo(2));
			Assert.That(_harness.Cache.GetIconsByPackId(pack.Id).Select(i => i.OriginalFileName),
				Is.EquivalentTo(["spinner.lottie", "loader.json"]));
		});
	}

	[Test]
	public async Task Import_FolderSelection_SkipsLooseJson()
	{
		var pack = await _harness.CreatePack();

		// A folder upload keeps its relative path in the multipart file name; a .json from in there was
		// never picked by hand, so it must not become an icon.
		var result = await _service.Import(pack.Id,
			null,
			Files(("assets/package.json", [1]), ("assets/spinner.lottie", [2])),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.That(_harness.Cache.GetIconsByPackId(pack.Id).Select(i => i.OriginalFileName),
			Is.EquivalentTo(["assets/spinner.lottie"]));
	}

	[Test]
	public async Task ImportFromPath_Directory_SkipsLooseJsonButTakesLottieFiles()
	{
		var pack = await _harness.CreatePack();
		var root = Path.Combine(_harness.Paths.BaseDirectory, "lottie-source");
		Directory.CreateDirectory(root);
		File.WriteAllBytes(Path.Combine(root, "spinner.lottie"), [1]);
		await File.WriteAllTextAsync(Path.Combine(root, "package.json"), "{}");

		var result = await _service.ImportFromPath(pack.Id, [root], CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.That(_harness.Cache.GetIconsByPackId(pack.Id).Select(i => i.OriginalFileName),
			Is.EquivalentTo(["spinner.lottie"]));
	}

	[Test]
	public async Task ImportSingle_WithoutPackId_ReturnsThePendingIconInTheDefaultPack()
	{
		var defaultPack = await _harness.CreatePack("Imported Icons", isDefault: true);

		var result = await _service.ImportSingle(null,
			new IconImportFile("Spotify.png", new MemoryStream([1, 2, 3])),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Reused, Is.False);
			Assert.That(result.Data!.Icon.PackId, Is.EqualTo(defaultPack.Id));
			Assert.That(result.Data!.Icon.Name, Is.EqualTo("Spotify"));
			Assert.That(result.Data!.Icon.ProcessingState, Is.EqualTo(IconProcessingState.Pending));
			Assert.That(_harness.Cache.GetIconsByPackId(defaultPack.Id), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task ImportSingle_MarksTheBatchSilentSoItRaisesNoNotification()
	{
		await _harness.CreatePack(isDefault: true);

		var result = await _service.ImportSingle(null,
			new IconImportFile("logo.png", new MemoryStream([1, 2, 3])),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var batch = _harness.Mediator.Published.OfType<IconImportProgressNotification>().First().Batch;
		Assert.That(batch.Silent, Is.True);
	}

	[Test]
	public async Task ImportSingle_RegistersTheIconAndEnqueuesConversion()
	{
		var pack = await _harness.CreatePack(isDefault: true);

		var result = await _service.ImportSingle(null,
			new IconImportFile("logo.png", new MemoryStream([1])),
			CancellationToken.None);

		var icon = result.Data!.Icon;
		Assert.Multiple(() =>
		{
			Assert.That(DrainChannel(), Is.EqualTo(new List<IconWorkItem> { new ProcessIconWorkItem(icon.Id) }));
			Assert.That(_harness.Mediator.Published.OfType<IconsAddedNotification>().Single().Icons.Single().Id,
				Is.EqualTo(icon.Id));
			Assert.That(_harness.Cache.GetIconById(icon.Id), Is.Not.Null);
			Assert.That(pack.Id, Is.EqualTo(icon.PackId));
		});
	}

	[Test]
	public async Task ImportSingle_UnsupportedFile_Fails()
	{
		await _harness.CreatePack(isDefault: true);

		var result = await _service.ImportSingle(null,
			new IconImportFile("app.exe", new MemoryStream([1])),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.UnsupportedFormat));
		});
	}

	[Test]
	public async Task ImportSingle_ReadOnlyPack_Fails()
	{
		var pack = await _harness.CreatePack(isReadOnly: true);

		var result = await _service.ImportSingle(pack.Id,
			new IconImportFile("logo.png", new MemoryStream([1])),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.False);
			Assert.That(result.Error, Is.EqualTo(IconError.PackReadOnly));
		});
	}

	[Test]
	public async Task Import_Archive_KeepsBatchDiscoveringAndEnqueuesExtraction()
	{
		var pack = await _harness.CreatePack();
		var zip = CreateZip(("icon.png", [1, 2, 3]));

		var result = await _service.Import(pack.Id,
			"icons.zip",
			Files(("icons.zip", zip)),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.State, Is.EqualTo(IconImportBatchState.Discovering));
			Assert.That(DrainChannel().OfType<ExtractBatchWorkItem>().Count(), Is.EqualTo(1));
			Assert.That(_harness.Storage.GetStagedArchivePaths(result.Data!.Id), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Import_EmptyFileList_CompletesImmediately()
	{
		var pack = await _harness.CreatePack();

		var result = await _service.Import(pack.Id, null, Files(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.BatchTracker.Get(result.Data!.Id), Is.Null, "batch must be finished");
			Assert.That(_harness.Mediator.Published.OfType<IconImportProgressNotification>().Any(), Is.True);
		});
	}

	[Test]
	public async Task ImportFromPath_Directory_DiscoversFilesRecursively()
	{
		var pack = await _harness.CreatePack();
		var root = Path.Combine(_harness.Paths.BaseDirectory, "source");
		Directory.CreateDirectory(Path.Combine(root, "nested"));
		File.WriteAllBytes(Path.Combine(root, "a.png"), [1]);
		File.WriteAllBytes(Path.Combine(root, "nested", "b.gif"), [2]);
		await File.WriteAllTextAsync(Path.Combine(root, "readme.md"), "skip me");

		var result = await _service.ImportFromPath(pack.Id, [root], CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		Assert.Multiple(() =>
		{
			Assert.That(icons, Has.Count.EqualTo(2));
			Assert.That(icons.Select(i => i.OriginalFileName),
				Is.EquivalentTo(["a.png", Path.Combine("nested", "b.gif")]));
		});
	}

	[Test]
	public async Task ImportFromPath_SeveralPaths_ImportIntoOneBatch()
	{
		var pack = await _harness.CreatePack();
		var first = Path.Combine(_harness.Paths.BaseDirectory, "first");
		var second = Path.Combine(_harness.Paths.BaseDirectory, "second");
		Directory.CreateDirectory(first);
		Directory.CreateDirectory(second);
		await File.WriteAllBytesAsync(Path.Combine(first, "a.png"), [1]);
		await File.WriteAllBytesAsync(Path.Combine(second, "b.gif"), [2]);

		var result = await _service.ImportFromPath(pack.Id, [first, second], CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		Assert.Multiple(() =>
		{
			Assert.That(icons, Has.Count.EqualTo(2));
			Assert.That(icons.Select(i => i.ImportBatchId).Distinct().Count(), Is.EqualTo(1), "one gesture, one batch");
			Assert.That(result.Data!.SourceName, Is.EqualTo("2 items"));
		});
	}

	[Test]
	public async Task ImportFromPath_SkipsAPathThatIsNotThere_AndImportsTheRest()
	{
		var pack = await _harness.CreatePack();
		var root = Path.Combine(_harness.Paths.BaseDirectory, "source");
		Directory.CreateDirectory(root);
		await File.WriteAllBytesAsync(Path.Combine(root, "a.png"), [1]);

		var result = await _service.ImportFromPath(pack.Id, ["/does/not/exist", root], CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.That(_harness.Cache.GetIconsByPackId(pack.Id), Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Silent_SurvivesThePersistenceRoundTrip()
	{
		var pack = await _harness.CreatePack();
		var batch = new IconImportBatchEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			State = IconImportBatchState.Processing,
			Silent = true
		};
		_harness.BatchTracker.Register(batch);

		var reloaded = new IconImportBatchTracker(_harness.BatchStore).LoadPersisted();

		Assert.That(reloaded.Single(b => b.Id == batch.Id).Silent, Is.True);
	}

	[Test]
	public async Task ImportFromPath_RecordsTheItemsItCouldNotRead()
	{
		var pack = await _harness.CreatePack();
		var root = Path.Combine(_harness.Paths.BaseDirectory, "source");
		Directory.CreateDirectory(root);
		await File.WriteAllBytesAsync(Path.Combine(root, "a.png"), [1]);

		var result = await _service.ImportFromPath(pack.Id, ["/does/not/exist", root], CancellationToken.None);

		Assert.That(result.Data!.Error, Does.Contain("could not be read"));
	}

	[Test]
	public async Task ImportFromPath_UnreadableSubfolder_IsNotReportedAsADroppedItem()
	{
		if (OperatingSystem.IsWindows())
		{
			Assert.Ignore("Directory permissions are not enforced the same way on Windows.");
			return;
		}

		var pack = await _harness.CreatePack();
		var root = Path.Combine(_harness.Paths.BaseDirectory, "source");
		var locked = Path.Combine(root, "locked");
		Directory.CreateDirectory(locked);
		await File.WriteAllBytesAsync(Path.Combine(root, "a.png"), [1]);
		File.SetUnixFileMode(locked, UnixFileMode.None);

		try
		{
			var result = await _service.ImportFromPath(pack.Id, [root], CancellationToken.None);

			Assert.Multiple(() =>
			{
				Assert.That(_harness.Cache.GetIconsByPackId(pack.Id), Has.Count.EqualTo(1));
				Assert.That(result.Data!.Error, Does.Contain("folder inside"));
				Assert.That(result.Data!.Error, Does.Not.Contain("dropped item"));
			});
		}
		finally
		{
			File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
		}
	}

	[Test]
	public async Task ImportFromPath_NoPaths_Fails()
	{
		var pack = await _harness.CreatePack();

		var result = await _service.ImportFromPath(pack.Id, [], CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(IconError.ValidationError));
	}

	[Test]
	public async Task ImportFromPath_MissingPath_Fails()
	{
		var pack = await _harness.CreatePack();

		var result = await _service.ImportFromPath(pack.Id, ["/does/not/exist"], CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(IconError.ValidationError));
	}

	[Test]
	public async Task Import_MacroDeckIconPack_MergesSynchronouslyIntoDestination()
	{
		var sourcePack = await _harness.CreatePack("Source");
		await AddReadyIcon(sourcePack, "ready-made");
		var archive = await ExportPack(sourcePack.Id);
		var target = await _harness.CreatePack("Target");

		var result = await _service.Import(target.Id,
			null,
			Files(("Source.macroDeckIconPack", archive)),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var (total, processed, failed) = _harness.BatchTracker.GetCounters(result.Data!.Id);
		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetIconsByPackId(target.Id).Single().ProcessingState,
				Is.EqualTo(IconProcessingState.Ready));
			Assert.That((total, processed, failed),
				Is.EqualTo((0, 0, 0)),
				"the finished batch must already be untracked");
			Assert.That(_harness.BatchTracker.Get(result.Data!.Id), Is.Null, "batch must be finished");
			Assert.That(result.Data!.State, Is.EqualTo(IconImportBatchState.Completed));
			Assert.That(_harness.Mediator.Published.OfType<IconsAddedNotification>()
					.Any(n => n.PackId == target.Id),
				Is.True);
			Assert.That(DrainChannel(), Is.Empty, "ready-made icons never enter the conversion pipeline");
		});
	}

	[Test]
	public async Task Import_CorruptMacroDeckIconPack_RecordsBatchError()
	{
		var target = await _harness.CreatePack("Target");

		var result = await _service.Import(target.Id,
			null,
			Files(("broken.macroDeckIconPack", [1, 2, 3])),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(result.Data!.Error, Does.Contain("broken.macroDeckIconPack"));
			Assert.That(_harness.Cache.GetIconsByPackId(target.Id), Is.Empty);
		});
	}

	[Test]
	public async Task ImportPacks_Archive_RegistersNewPacksBatchAndStagesIt()
	{
		await _harness.CreatePack("Imported Icons", isDefault: true);
		var zip = CreateZip(("icon.png", [1, 2]));

		var result = await _service.ImportPacks(Files(("cool-pack.zip", zip)), CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var batch = result.Data!.Batch;
		Assert.Multiple(() =>
		{
			Assert.That(batch, Is.Not.Null);
			Assert.That(batch!.Mode, Is.EqualTo(IconImportMode.NewPacks));
			Assert.That(batch.ExplicitDestination, Is.False);
			Assert.That(result.Data!.RestoredPacks, Is.Empty);
			Assert.That(_harness.Storage.GetStagedArchivePaths(batch.Id), Has.Count.EqualTo(1));
			Assert.That(DrainChannel().OfType<ExtractBatchWorkItem>().Count(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task ImportPacks_OnlyMacroDeckIconPack_RestoresWithoutBatch()
	{
		var sourcePack = await _harness.CreatePack("Source");
		await AddReadyIcon(sourcePack, "a");
		var archive = await ExportPack(sourcePack.Id);

		var result = await _service.ImportPacks(Files(("Source.macroDeckIconPack", archive)),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Batch, Is.Null);
			Assert.That(result.Data!.RestoredPacks, Has.Count.EqualTo(1));
			Assert.That(result.Data!.RestoredPacks[0].SourceType, Is.EqualTo(IconPackSourceType.MacroDeckImport));
		});
	}

	[Test]
	public async Task ImportPacks_UnsupportedOrNoFiles_FailsWithValidationError()
	{
		await _harness.CreatePack(isDefault: true);

		var empty = await _service.ImportPacks(Files(), CancellationToken.None);
		var unsupported = await _service.ImportPacks(Files(("loose.png", [1])), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(empty.Error, Is.EqualTo(IconError.ValidationError));
			Assert.That(unsupported.Error, Is.EqualTo(IconError.ValidationError));
		});
	}

	[Test]
	public async Task ImportPacks_OnlyFailedRestores_FailsWithInvalidArchive()
	{
		var result = await _service.ImportPacks(Files(("broken.macroDeckIconPack", [1])),
			CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(IconError.InvalidArchive));
	}

	private async Task AddReadyIcon(IconPackEntity pack, string name)
	{
		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			Name = name,
			ProcessingState = IconProcessingState.Ready,
			AvailableSizes = [],
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(pack.Id, [icon]);
		await _harness.Storage.WriteVariant(pack.Id,
			icon.Id,
			IconVariants.Master,
			new byte[] { 1 },
			CancellationToken.None);
	}

	private async Task<byte[]> ExportPack(Guid packId)
	{
		var stream = new MemoryStream();
		var result = await _harness.CreateExportService().Export(packId, stream, CancellationToken.None);
		Assert.That(result.Success, Is.True);
		return stream.ToArray();
	}

	// --- Deduplication by content hash (issue #284) ---

	private static readonly byte[] _stagedBytes = [1, 2, 3];
	private static readonly string[] _distinctBatchNames = ["a", "c"];

	[Test]
	public async Task ImportSingle_SameBytesTwice_ReturnsTheFirstIcon_WithoutImportingAgain()
	{
		var pack = await _harness.CreatePack(isDefault: true);
		var first = await _harness.AddReadyIcon(pack.Id, "logo", [1, 2, 3]);
		_harness.Mediator.Published.Clear();
		DrainChannel();

		var result = await _service.ImportSingle(null,
			new IconImportFile("logo.png", new MemoryStream([1, 2, 3])),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Reused, Is.True);
			Assert.That(result.Data!.Icon.Id, Is.EqualTo(first.Id));
			Assert.That(_harness.Cache.GetIconsByPackId(pack.Id), Has.Count.EqualTo(1), "no second icon");
			Assert.That(DrainChannel(), Is.Empty, "nothing is queued for conversion");
			Assert.That(_harness.Mediator.Published, Is.Empty, "a reuse raises no import events at all");
			Assert.That(_harness.Storage.EnumerateStagedBatchIds(), Is.Empty, "the staged copy is discarded");
		});
	}

	[Test]
	public async Task ImportSingle_DifferentBytes_StillCreatesANewIcon()
	{
		var pack = await _harness.CreatePack(isDefault: true);
		await _harness.AddReadyIcon(pack.Id, "logo", [1, 2, 3]);

		var result = await _service.ImportSingle(null,
			new IconImportFile("logo.png", new MemoryStream([1, 2, 4])),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Reused, Is.False);
			Assert.That(_harness.Cache.GetIconsByPackId(pack.Id), Has.Count.EqualTo(2));
		});
	}

	[Test]
	public async Task ImportSingle_WithoutPackId_ReusesAnIconFromAnyPack()
	{
		await _harness.CreatePack("Imported Icons", isDefault: true);
		var other = await _harness.CreatePack("Stream Deck", isReadOnly: true);
		var existing = await _harness.AddReadyIcon(other.Id, "logo", [9, 9]);

		var result = await _service.ImportSingle(null,
			new IconImportFile("logo.png", new MemoryStream([9, 9])),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Reused, Is.True);
			Assert.That(result.Data!.Icon.Id, Is.EqualTo(existing.Id));
		});
	}

	[Test]
	public async Task ImportSingle_WithPackId_DoesNotReuseAnIconFromAnotherPack()
	{
		var target = await _harness.CreatePack("Target");
		var other = await _harness.CreatePack("Other");
		await _harness.AddReadyIcon(other.Id, "logo", [9, 9]);

		var result = await _service.ImportSingle(target.Id,
			new IconImportFile("logo.png", new MemoryStream([9, 9])),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Reused, Is.False);
			Assert.That(_harness.Cache.GetIconsByPackId(target.Id), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Import_IdenticalFilesInOneBatch_CollapseToOneIcon()
	{
		var pack = await _harness.CreatePack();

		// Nothing is Ready during a bulk import, so this only works because in-flight content is claimed
		// as well - the finished-icon index alone would let all three through.
		var result = await _service.Import(pack.Id,
			"drop",
			Files(("a.png", [7, 7]), ("b.png", [7, 7]), ("c.png", [8, 8])),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.That(_harness.Cache.GetIconsByPackId(pack.Id).Select(icon => icon.Name),
			Is.EquivalentTo(_distinctBatchNames));
	}

	[Test]
	public async Task Import_ContentTheDestinationPackAlreadyHas_IsSkipped()
	{
		var pack = await _harness.CreatePack();
		await _harness.AddReadyIcon(pack.Id, "logo", [4, 5]);

		var result = await _service.Import(pack.Id, "drop", Files(("copy.png", [4, 5])), CancellationToken.None);

		Assert.That(result.Success, Is.True);
		Assert.That(_harness.Cache.GetIconsByPackId(pack.Id), Has.Count.EqualTo(1));
	}

	[Test]
	public async Task Import_ContentAnotherPackHas_IsStillImported()
	{
		var target = await _harness.CreatePack("Target");
		var other = await _harness.CreatePack("Other");
		await _harness.AddReadyIcon(other.Id, "logo", [4, 5]);

		await _service.Import(target.Id, "drop", Files(("copy.png", [4, 5])), CancellationToken.None);

		Assert.That(_harness.Cache.GetIconsByPackId(target.Id), Has.Count.EqualTo(1));
	}

	[TestCase(IconProcessingState.Pending)]
	[TestCase(IconProcessingState.Failed)]
	[TestCase(IconProcessingState.Processing)]
	public async Task ImportSingle_NeverReusesAnIconThatIsNotReady(IconProcessingState state)
	{
		var pack = await _harness.CreatePack(isDefault: true);
		var existing = await _harness.AddReadyIcon(pack.Id, "logo", [1, 2, 3]);
		existing.ProcessingState = state;
		await _harness.Cache.UpdateIcon(existing);

		var result = await _service.ImportSingle(null,
			new IconImportFile("logo.png", new MemoryStream([1, 2, 3])),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Reused, Is.False);
			Assert.That(result.Data!.Icon.Id, Is.Not.EqualTo(existing.Id));
		});
	}

	[Test]
	public async Task ImportSingle_WhenTheCandidatesMasterFileIsGone_ImportsNormally()
	{
		var pack = await _harness.CreatePack(isDefault: true);
		var existing = await _harness.AddReadyIcon(pack.Id, "logo", [1, 2, 3]);
		_harness.Storage.DeleteIconFiles(pack.Id, existing.Id);

		var result = await _service.ImportSingle(null,
			new IconImportFile("logo.png", new MemoryStream([1, 2, 3])),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Reused, Is.False);
			Assert.That(result.Data!.Icon.Id, Is.Not.EqualTo(existing.Id));
		});
	}

	[Test]
	public async Task CancelBatch_ReleasesTheContentClaimOfTheIconsItRolledBack()
	{
		var pack = await _harness.CreatePack();
		var first = await _service.Import(pack.Id, "drop", Files(("logo.png", [3, 3])), CancellationToken.None);
		await _service.CancelBatch(first.Data!.Id, CancellationToken.None);

		var second = await _service.Import(pack.Id, "drop", Files(("logo.png", [3, 3])), CancellationToken.None);

		Assert.That(second.Data!.Total, Is.EqualTo(1), "the same bytes must be importable again");
		Assert.That(_harness.Cache.GetIconsByPackId(pack.Id), Has.Count.EqualTo(1));
	}

	[Test]
	public async Task DeletingAnIconMidImport_ReleasesItsContentClaim()
	{
		var pack = await _harness.CreatePack();
		var iconService = new IconService(_harness.Cache,
			_harness.Storage,
			_harness.FallbackStore,
			_harness.Coalescer,
			_harness.Mediator);
		await _service.Import(pack.Id, "drop", Files(("logo.png", [5, 5])), CancellationToken.None);
		var pending = _harness.Cache.GetIconsByPackId(pack.Id).Single();
		await iconService.Delete(pending.Id);

		var second = await _service.Import(pack.Id, "drop", Files(("logo.png", [5, 5])), CancellationToken.None);

		Assert.That(second.Data!.Total, Is.EqualTo(1), "the same bytes must be importable again");
	}

	[Test]
	public async Task Import_RecordsHowManyDuplicateFilesItSkipped()
	{
		var pack = await _harness.CreatePack();
		await _harness.AddReadyIcon(pack.Id, "logo", [4, 5]);

		var result = await _service.Import(pack.Id,
			"drop",
			Files(("copy.png", [4, 5]), ("other.png", [4, 5]), ("new.png", [6, 6])),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Data!.Skipped, Is.EqualTo(2));
			Assert.That(result.Data!.Total, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task ImportSingle_RecordsTheHashOfTheStagedBytes()
	{
		await _harness.CreatePack(isDefault: true);

		var result = await _service.ImportSingle(null,
			new IconImportFile("logo.png", new MemoryStream([1, 2, 3])),
			CancellationToken.None);

		// Available before conversion runs, which is the point: the reuse decision cannot wait for it.
		Assert.That(result.Data!.Icon.SourceContentHash,
			Is.EqualTo(SourceContentHash.Compute(_stagedBytes).Value));
	}

	// --- Application icon extraction (issue #402) ---

	[Test]
	public async Task ImportFromPath_DirectlyDroppedApplication_BecomesOneIconNamedAfterTheExtraction()
	{
		var pack = await _harness.CreatePack();
		var bundle = Path.Combine(_harness.Paths.BaseDirectory, "Spotify.app");
		Directory.CreateDirectory(Path.Combine(bundle, "Contents", "Resources"));
		await File.WriteAllBytesAsync(Path.Combine(bundle, "Contents", "Resources", "inner.png"), [99]);
		_extractor.Results[bundle]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([10, 10], "Spotify.png"));

		var result = await _service.ImportFromPath(pack.Id, [bundle], CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		Assert.Multiple(() =>
		{
			Assert.That(icons, Has.Count.EqualTo(1));
			Assert.That(icons[0].Name, Is.EqualTo("Spotify"));
			Assert.That(icons[0].OriginalFileName, Is.EqualTo("Spotify.png"));
			Assert.That(_extractor.Requested, Is.EqualTo(new List<string> { bundle }));
		});
		Assert.That(await StagedBytes(result.Data!.Id, icons[0].Id), Is.EqualTo(new byte[] { 10, 10 }));
	}

	[Test]
	public async Task ImportFromPath_DirectlyDroppedDll_IsImported()
	{
		var pack = await _harness.CreatePack();
		var dllPath = Path.Combine(_harness.Paths.BaseDirectory, "Shell32.dll");
		await File.WriteAllBytesAsync(dllPath, [1, 2, 3]);
		_extractor.Results[dllPath]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([11, 11], "Shell32.png"));

		var result = await _service.ImportFromPath(pack.Id, [dllPath], CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		Assert.That(icons, Has.Count.EqualTo(1));
		Assert.That(icons[0].Name, Is.EqualTo("Shell32"));
	}

	[Test]
	public async Task ImportFromPath_FolderSweep_CoversExactSetAndExcludesDll()
	{
		var pack = await _harness.CreatePack();
		var root = Path.Combine(_harness.Paths.BaseDirectory, "sweep");
		Directory.CreateDirectory(root);
		await File.WriteAllBytesAsync(Path.Combine(root, "a.png"), [1]);
		await File.WriteAllBytesAsync(Path.Combine(root, "spinner.lottie"), [2]);
		await File.WriteAllBytesAsync(Path.Combine(root, "Tool.exe"), [3]);
		await File.WriteAllBytesAsync(Path.Combine(root, "Shortcut.lnk"), [4]);
		await File.WriteAllBytesAsync(Path.Combine(root, "Site.url"), [5]);
		await File.WriteAllBytesAsync(Path.Combine(root, "Editor.desktop"), [6]);
		await File.WriteAllBytesAsync(Path.Combine(root, "Icon.ico"), [7]);
		await File.WriteAllBytesAsync(Path.Combine(root, "Suite.icns"), [8]);
		await File.WriteAllBytesAsync(Path.Combine(root, "Resource.dll"), [9]);
		await File.WriteAllTextAsync(Path.Combine(root, "readme.md"), "skip");
		await File.WriteAllTextAsync(Path.Combine(root, "package.json"), "{}");
		var bundle = Path.Combine(root, "Spotify.app");
		Directory.CreateDirectory(Path.Combine(bundle, "Contents", "Resources"));
		await File.WriteAllBytesAsync(Path.Combine(bundle, "Contents", "Resources", "AppIcon.icns"), [10]);
		await File.WriteAllBytesAsync(Path.Combine(bundle, "Contents", "Resources", "logo.png"), [11]);

		byte payload = 20;
		foreach (var relative in new[]
			{ "Tool.exe", "Shortcut.lnk", "Site.url", "Editor.desktop", "Icon.ico", "Suite.icns" })
		{
			_extractor.Results[Path.Combine(root, relative)] = Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(
				new ExtractedAppIcon([payload++], Path.GetFileNameWithoutExtension(relative) + ".png"));
		}

		_extractor.Results[bundle]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([payload], "Spotify.png"));

		var result = await _service.ImportFromPath(pack.Id, [root], CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var names = _harness.Cache.GetIconsByPackId(pack.Id).Select(i => i.Name).ToList();
		string[] expectedNames = ["a", "spinner", "Tool", "Shortcut", "Site", "Editor", "Icon", "Suite", "Spotify"];
		Assert.Multiple(() =>
		{
			Assert.That(names, Is.EquivalentTo(expectedNames));
			Assert.That(names, Does.Not.Contain("Resource"));
			Assert.That(names, Does.Not.Contain("readme"));
			Assert.That(names, Does.Not.Contain("package"));
			Assert.That(names, Does.Not.Contain("logo"));
			Assert.That(_extractor.Requested.Any(p => p.EndsWith(".png", StringComparison.Ordinal) ||
					p.EndsWith(".lottie", StringComparison.Ordinal) ||
					p.EndsWith(".md", StringComparison.Ordinal) ||
					p.EndsWith(".json", StringComparison.Ordinal) ||
					p.EndsWith(".dll", StringComparison.Ordinal)),
				Is.False);
		});
	}

	[Test]
	public async Task ImportFromPath_AppInsideSweptFolder_IsALeaf()
	{
		var pack = await _harness.CreatePack();
		var root = Path.Combine(_harness.Paths.BaseDirectory, "leaf-sweep");
		var bundle = Path.Combine(root, "Spotify.app");
		Directory.CreateDirectory(Path.Combine(bundle, "Contents", "Resources"));
		Directory.CreateDirectory(Path.Combine(bundle, "Contents", "MacOS"));
		await File.WriteAllBytesAsync(Path.Combine(bundle, "Contents", "Resources", "AppIcon.icns"), [1]);
		await File.WriteAllBytesAsync(Path.Combine(bundle, "Contents", "Resources", "logo.png"), [2]);
		await File.WriteAllBytesAsync(Path.Combine(bundle, "Contents", "Resources", "banner.jpg"), [3]);
		await File.WriteAllTextAsync(Path.Combine(bundle, "Contents", "MacOS", "spotify"), "binary");
		_extractor.Results[bundle]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([30, 30], "Spotify.png"));

		var result = await _service.ImportFromPath(pack.Id, [root], CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		Assert.Multiple(() =>
		{
			Assert.That(icons, Has.Count.EqualTo(1));
			Assert.That(icons[0].Name, Is.EqualTo("Spotify"));
			Assert.That(_extractor.Requested, Is.EqualTo(new List<string> { bundle }));
		});
		Assert.That(await StagedBytes(result.Data!.Id, icons[0].Id), Is.EqualTo(new byte[] { 30, 30 }));
	}

	[Test]
	public async Task ImportFromPath_UnreadableSource_IsFailureNotDuplicate()
	{
		var pack = await _harness.CreatePack();
		var root = Path.Combine(_harness.Paths.BaseDirectory, "mixed");
		Directory.CreateDirectory(root);
		var good = Path.Combine(root, "Good.exe");
		var broken = Path.Combine(root, "Broken.exe");
		await File.WriteAllBytesAsync(good, [1]);
		await File.WriteAllBytesAsync(broken, [2]);
		_extractor.Results[good]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([40, 40], "Good.png"));
		_extractor.Results[broken] = Domain.Common.Result.Fail<ExtractedAppIcon, IconError>(IconError.UnsupportedFormat,
			"no icon in Broken.exe");

		var result = await _service.ImportFromPath(pack.Id, [root], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			var icons = _harness.Cache.GetIconsByPackId(pack.Id);
			Assert.That(icons, Has.Count.EqualTo(1));
			Assert.That(icons[0].Name, Is.EqualTo("Good"));
			Assert.That(result.Data!.Skipped, Is.EqualTo(0));
			Assert.That(result.Data!.Error, Does.Contain("Broken.exe"));
		});
	}

	[Test]
	public async Task ImportFromPath_DedupUsesExtractedBytesNotSourceFile()
	{
		var pack = await _harness.CreatePack();
		await _harness.AddReadyIcon(pack.Id, "existing", [7, 7]);
		var root = Path.Combine(_harness.Paths.BaseDirectory, "dedup");
		Directory.CreateDirectory(root);
		var appOne = Path.Combine(root, "AppOne.exe");
		var appTwo = Path.Combine(root, "AppTwo.exe");
		await File.WriteAllBytesAsync(appOne, [50]);
		await File.WriteAllBytesAsync(appTwo, [51]);
		_extractor.Results[appOne]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([7, 7], "AppOne.png"));
		_extractor.Results[appTwo]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([7, 7], "AppTwo.png"));

		var result = await _service.ImportFromPath(pack.Id, [root], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetIconsByPackId(pack.Id),
				Has.Count.EqualTo(1),
				"no new icons besides the seeded one");
			Assert.That(result.Data!.Skipped, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task ImportFromPath_ReadOnlyDestination_RefusedBeforeExtraction()
	{
		var pack = await _harness.CreatePack(isReadOnly: true);
		var exePath = Path.Combine(_harness.Paths.BaseDirectory, "Tool.exe");
		await File.WriteAllBytesAsync(exePath, [1]);

		var result = await _service.ImportFromPath(pack.Id, [exePath], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(IconError.PackReadOnly));
			Assert.That(_harness.Cache.GetIconsByPackId(pack.Id), Is.Empty);
			Assert.That(_extractor.Requested, Is.Empty);
		});
	}

	[Test]
	public async Task ImportFromPath_NothingImportable_ImportsNothingAndAsksNoExtractor()
	{
		var pack = await _harness.CreatePack();
		var notesPath = Path.Combine(_harness.Paths.BaseDirectory, "notes.txt");
		await File.WriteAllTextAsync(notesPath, "hello");

		var result = await _service.ImportFromPath(pack.Id, [notesPath], CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetIconsByPackId(pack.Id), Is.Empty);
			Assert.That(_extractor.Requested, Is.Empty);
		});
	}

	[Test]
	public async Task Import_UploadedContainers_AreExtracted()
	{
		var pack = await _harness.CreatePack();
		_extractor.Results["App.ico"]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([1, 1], "App.png"));
		_extractor.Results["Suite.icns"]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([2, 2], "Suite.png"));
		_extractor.Results["Tool.exe"]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([3, 3], "Tool.png"));
		_extractor.Results["Shell32.dll"]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([4, 4], "Shell32.png"));

		var result = await _service.Import(pack.Id,
			null,
			Files(("App.ico", [10]),
				("Suite.icns", [11]),
				("Tool.exe", [12]),
				("Shell32.dll", [13]),
				("plain.png", [14])),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		string[] expectedNames = ["App", "Suite", "Tool", "Shell32", "plain"];
		string[] expectedRequested = ["App.ico", "Suite.icns", "Tool.exe", "Shell32.dll"];
		Assert.Multiple(() =>
		{
			Assert.That(icons.Select(i => i.Name), Is.EquivalentTo(expectedNames));
			Assert.That(_extractor.RequestedContent, Is.EquivalentTo(expectedRequested));
		});

		var appIcon = icons.Single(i => i.Name == "App");
		Assert.That(await StagedBytes(result.Data!.Id, appIcon.Id), Is.EqualTo(new byte[] { 1, 1 }));
	}

	[Test]
	public async Task Import_UploadedShortcuts_AreRefused()
	{
		var pack = await _harness.CreatePack();

		var result = await _service.Import(pack.Id,
			null,
			Files(("Shortcut.lnk", [1]), ("Site.url", [2]), ("Editor.desktop", [3]), ("ok.png", [4])),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		string[] expectedNames = ["ok"];
		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetIconsByPackId(pack.Id).Select(i => i.Name), Is.EquivalentTo(expectedNames));
			Assert.That(_extractor.RequestedContent, Is.Empty);
		});
	}

	[Test]
	public async Task Import_WebkitDirectoryUpload_YieldsImagesOnly()
	{
		var pack = await _harness.CreatePack();

		var result = await _service.Import(pack.Id,
			null,
			Files(("apps/Tool.exe", [1]), ("apps/App.ico", [2]), ("apps/logo.png", [3]), ("apps/package.json", [4])),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		Assert.Multiple(() =>
		{
			Assert.That(icons, Has.Count.EqualTo(1));
			Assert.That(icons[0].OriginalFileName, Is.EqualTo("apps/logo.png"));
			Assert.That(_extractor.RequestedContent, Is.Empty);
		});
	}

	[Test]
	public async Task Import_UploadedContent_IsConsumedForwardOnly()
	{
		var pack = await _harness.CreatePack();
		_extractor.EchoContent = true;

		var result = await _service.Import(pack.Id,
			null,
			ForwardOnlyFiles(("App.ico", [1, 2, 3, 4, 5])),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var icons = _harness.Cache.GetIconsByPackId(pack.Id);
		Assert.That(icons, Has.Count.EqualTo(1));
		Assert.That(await StagedBytes(result.Data!.Id, icons[0].Id), Is.EqualTo(new byte[] { 1, 2, 3, 4, 5 }));
	}

	[Test]
	public async Task Import_UploadedContentExtractionFailure_IsNotADuplicate()
	{
		var pack = await _harness.CreatePack();
		_extractor.Results["Good.ico"]
			= Domain.Common.Result.Ok<ExtractedAppIcon, IconError>(new ExtractedAppIcon([1, 1], "Good.png"));
		_extractor.Results["Broken.ico"] = Domain.Common.Result.Fail<ExtractedAppIcon, IconError>(
			IconError.UnsupportedFormat,
			"no icon in Broken.ico");

		var result = await _service.Import(pack.Id,
			null,
			Files(("Good.ico", [1]), ("Broken.ico", [2])),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetIconsByPackId(pack.Id), Has.Count.EqualTo(1));
			Assert.That(result.Data!.Skipped, Is.EqualTo(0));
			Assert.That(result.Data!.Error, Does.Contain("Broken.ico"));
		});
	}

	private async Task<byte[]> StagedBytes(Guid batchId, Guid iconId)
	{
		await using var staged = _harness.Storage.OpenStagedOriginal(batchId, iconId);
		using var buffer = new MemoryStream();
		await staged!.CopyToAsync(buffer);
		return buffer.ToArray();
	}

	private static async IAsyncEnumerable<IconImportFile> ForwardOnlyFiles(params (string Name, byte[] Content)[] files)
	{
		foreach (var (name, content) in files)
		{
			yield return new IconImportFile(name, new ForwardOnlyStream(content));
		}

		await Task.CompletedTask;
	}

	private sealed class ForwardOnlyStream : Stream
	{
		private readonly MemoryStream _inner;

		public ForwardOnlyStream(byte[] content) => _inner = new MemoryStream(content);

		public override bool CanRead => true;

		public override bool CanSeek => false;

		public override bool CanWrite => false;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

		public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
			=> _inner.ReadAsync(buffer, offset, count, cancellationToken);

		public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
			=> _inner.ReadAsync(buffer, cancellationToken);

		public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

		public override void SetLength(long value) => throw new NotSupportedException();

		public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

		public override void Flush()
		{
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

	private List<IconWorkItem> DrainChannel()
	{
		var items = new List<IconWorkItem>();
		while (_harness.ProcessingChannel.Reader.TryRead(out var item))
		{
			items.Add(item);
		}

		return items;
	}
}
