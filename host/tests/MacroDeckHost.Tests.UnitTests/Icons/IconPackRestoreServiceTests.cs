using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json.Nodes;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconPackRestoreServiceTests
{
	private static readonly int[] _size128 = [128];
	private static readonly string[] _expectedRestoredFiles = ["pack.json", "master.webp"];

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
	public async Task RestoreAsNewPack_RoundTripsAnExport_WithFreshIds()
	{
		var sourcePack = await _harness.CreatePack("Exported");
		sourcePack.Author = "Author";
		sourcePack.Version = "1.0";
		await _harness.Cache.AddOrUpdatePack(sourcePack);
		var sourceIcon = await AddReadyIcon(sourcePack, "star", sizes: [128]);
		var archive = await Export(sourcePack.Id);

		var result = await _harness.RestoreService.RestoreAsNewPack("Exported.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var restored = result.Data!;
		var icons = _harness.Cache.GetIconsByPackId(restored.Id);
		Assert.Multiple(() =>
		{
			Assert.That(restored.Id, Is.Not.EqualTo(sourcePack.Id));
			Assert.That(restored.Name, Is.EqualTo("Exported"));
			Assert.That(restored.Author, Is.EqualTo("Author"));
			Assert.That(restored.Version, Is.EqualTo("1.0"));
			Assert.That(restored.SourceType, Is.EqualTo(IconPackSourceType.MacroDeckImport));
			Assert.That(icons, Has.Count.EqualTo(1));
			Assert.That(icons[0].Id, Is.Not.EqualTo(sourceIcon.Id));
			Assert.That(icons[0].Name, Is.EqualTo("star"));
			Assert.That(icons[0].ProcessingState, Is.EqualTo(IconProcessingState.Ready));
			Assert.That(icons[0].AvailableSizes, Is.EqualTo(_size128));
			Assert.That(_harness.ProcessingChannel.Reader.TryRead(out _),
				Is.False,
				"a restore must never enqueue conversion work");
			Assert.That(_harness.Mediator.Published.OfType<IconPackCreatedNotification>()
					.Any(n => n.Pack.Id == restored.Id && n.IconCount == 1),
				Is.True);
		});

		await using var master = _harness.Storage.OpenVariant(restored.Id, icons[0].Id, IconVariants.Master);
		await using var variant = _harness.Storage.OpenVariant(restored.Id, icons[0].Id, "128");
		Assert.Multiple(() =>
		{
			Assert.That(master, Is.Not.Null);
			Assert.That(variant, Is.Not.Null);
		});
	}

	[Test]
	public async Task RestoreAsNewPack_SameArchiveTwice_CreatesTwoIndependentPacks()
	{
		var sourcePack = await _harness.CreatePack("Twice");
		await AddReadyIcon(sourcePack, "a", sizes: []);
		var archive = await Export(sourcePack.Id);

		var first = await _harness.RestoreService.RestoreAsNewPack("Twice.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);
		var second = await _harness.RestoreService.RestoreAsNewPack("Twice.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(first.Success && second.Success, Is.True);
			Assert.That(first.Data!.Id, Is.Not.EqualTo(second.Data!.Id));
			Assert.That(_harness.Cache.GetIconsByPackId(first.Data!.Id).Single().Id,
				Is.Not.EqualTo(_harness.Cache.GetIconsByPackId(second.Data!.Id).Single().Id));
		});
	}

	[Test]
	public async Task RestoreAsNewPack_WithoutManifest_FailsAsInvalidArchive()
	{
		var archive = CreateZip(("readme.txt", [1]));

		var result = await _harness.RestoreService.RestoreAsNewPack("x.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.EqualTo(IconError.InvalidArchive));
			Assert.That(_harness.Cache.GetAllPacks(), Is.Empty);
		});
	}

	[Test]
	public async Task RestoreAsNewPack_CorruptFile_FailsAsInvalidArchive()
	{
		var result = await _harness.RestoreService.RestoreAsNewPack("x.macroDeckIconPack",
			new MemoryStream(Encoding.UTF8.GetBytes("not a zip")),
			CancellationToken.None);

		Assert.That(result.Error, Is.EqualTo(IconError.InvalidArchive));
	}

	[Test]
	public async Task RestoreAsNewPack_IgnoresHostileAndUnrelatedEntries()
	{
		var iconId = Guid.CreateVersion7();
		var manifest = $$"""
						 {
						 	"Id": "{{Guid.CreateVersion7()}}",
						 	"Name": "Hostile",
						 	"Icons": [{ "Id": "{{iconId}}", "Name": "good", "State": "Ready", "AvailableSizes": [] }]
						 }
						 """;
		var archive = CreateZip(("pack.json", Encoding.UTF8.GetBytes(manifest)),
			($"icons/{iconId}/master.webp", [1, 2]),
			("../evil.webp", [3]),
			($"icons/{iconId}/../../escape.webp", [4]),
			($"icons/{iconId}/notavariant.webp", [5]),
			("unrelated/file.bin", [6]));

		var result = await _harness.RestoreService.RestoreAsNewPack("Hostile.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var packDirectory = Path.Combine(_harness.Paths.IconPacksDirectory, result.Data!.Id.ToString());
		var files = Directory.EnumerateFiles(packDirectory, "*", SearchOption.AllDirectories)
			.Select(Path.GetFileName)
			.ToList();
		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetIconsByPackId(result.Data!.Id), Has.Count.EqualTo(1));
			Assert.That(files, Is.EquivalentTo(_expectedRestoredFiles));
			Assert.That(File.Exists(Path.Combine(_harness.Paths.BaseDirectory, "evil.webp")), Is.False);
			Assert.That(File.Exists(Path.Combine(_harness.Paths.BaseDirectory, "escape.webp")), Is.False);
		});
	}

	[Test]
	public async Task RestoreAsNewPack_ArchiveCarryingCertificateEntries_StillImports()
	{
		var iconId = Guid.CreateVersion7();
		var manifest = $$"""
						 {
						 	"Id": "{{Guid.CreateVersion7()}}",
						 	"Name": "Signed",
						 	"Icons": [{ "Id": "{{iconId}}", "Name": "good", "State": "Ready", "AvailableSizes": [] }]
						 }
						 """;
		var archive = CreateZip(("pack.json", Encoding.UTF8.GetBytes(manifest)),
			($"icons/{iconId}/master.webp", [1, 2]),
			("certificate.json", "{}"u8.ToArray()),
			("certificate.sig", [9, 9, 9]));

		var result = await _harness.RestoreService.RestoreAsNewPack("Signed.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetIconsByPackId(result.Data!.Id), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task RestoreAsNewPack_ArchiveWrittenBeforeFilesExisted_StillImportsUnchanged()
	{
		var sourcePack = await _harness.CreatePack("Legacy");
		await AddReadyIcon(sourcePack, "good", sizes: []);
		var archive = await Export(sourcePack.Id);

		var manifestText = ReadZipEntryText(archive, "pack.json");
		Assert.That(manifestText, Does.Contain("\"files\""), "sanity check: a fresh export does declare files");
		var strippedManifest = JsonNode.Parse(manifestText)!.AsObject();
		strippedManifest.Remove("files");
		var legacyArchive
			= ReplaceZipEntry(archive, "pack.json", Encoding.UTF8.GetBytes(strippedManifest.ToJsonString()));

		var result = await _harness.RestoreService.RestoreAsNewPack("Legacy.macroDeckIconPack",
			new MemoryStream(legacyArchive),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Success, Is.True);
			Assert.That(_harness.Cache.GetIconsByPackId(result.Data!.Id), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task MergeIntoPack_AddsReIdedReadyIconsToExistingPack()
	{
		var sourcePack = await _harness.CreatePack("Source");
		var sourceIcon = await AddReadyIcon(sourcePack, "merge-me", sizes: [256]);
		var archive = await Export(sourcePack.Id);
		var target = await _harness.CreatePack("Target");
		var batchId = Guid.CreateVersion7();

		var result = await _harness.RestoreService.MergeIntoPack(target.Id,
			batchId,
			"Source.macroDeckIconPack",
			new MemoryStream(archive),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var merged = _harness.Cache.GetIconsByPackId(target.Id).Single();
		Assert.Multiple(() =>
		{
			Assert.That(merged.Id, Is.Not.EqualTo(sourceIcon.Id));
			Assert.That(merged.Name, Is.EqualTo("merge-me"));
			Assert.That(merged.ProcessingState, Is.EqualTo(IconProcessingState.Ready));
			Assert.That(merged.ImportBatchId, Is.EqualTo(batchId));
			Assert.That(target.Name, Is.EqualTo("Target"), "merge must not touch the target pack metadata");
		});

		await using var master = _harness.Storage.OpenVariant(target.Id, merged.Id, IconVariants.Master);
		Assert.That(master, Is.Not.Null);
	}

	[Test]
	public async Task MergeIntoPack_UnknownOrReadOnlyTarget_Fails()
	{
		var readOnly = await _harness.CreatePack(isReadOnly: true);

		var missing = await _harness.RestoreService.MergeIntoPack(Guid.NewGuid(),
			Guid.NewGuid(),
			"x",
			new MemoryStream(),
			CancellationToken.None);
		var rejected = await _harness.RestoreService.MergeIntoPack(readOnly.Id,
			Guid.NewGuid(),
			"x",
			new MemoryStream(),
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(missing.Error, Is.EqualTo(IconError.PackNotFound));
			Assert.That(rejected.Error, Is.EqualTo(IconError.PackReadOnly));
		});
	}

	private async Task<IconEntity> AddReadyIcon(IconPackEntity pack, string name, int[] sizes)
	{
		var master = Encoding.UTF8.GetBytes($"master:{name}");
		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			Name = name,
			MasterContentHash = MasterContentHash.Compute(master).Value,
			ProcessingState = IconProcessingState.Ready,
			AvailableSizes = sizes,
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(pack.Id, [icon]);
		await _harness.Storage.WriteVariant(pack.Id, icon.Id, IconVariants.Master, master, CancellationToken.None);
		foreach (var size in sizes)
		{
			await _harness.Storage.WriteVariant(pack.Id,
				icon.Id,
				size.ToString(CultureInfo.InvariantCulture),
				new byte[] { 4 },
				CancellationToken.None);
		}

		return icon;
	}

	// The manifest's master hash describes the bytes bundled beside it, so a mismatch means the archive
	// was corrupted or edited - and writing an icon whose pixels are not what its entry claims is worse
	// than leaving it out (issue #284).
	[Test]
	public async Task RestoreAsNewPack_BundledMasterThatFailsItsDeclaredHash_IsSkipped()
	{
		var sourcePack = await _harness.CreatePack("Exported");
		await AddReadyIcon(sourcePack, "good", sizes: []);
		var tampered = ReplaceZipEntry(await Export(sourcePack.Id), "master.webp", "tampered"u8.ToArray());

		var result = await _harness.RestoreService.RestoreAsNewPack("Exported.macroDeckIconPack",
			new MemoryStream(tampered),
			CancellationToken.None);

		Assert.That(result.Success, Is.True);
		var restoredPack = result.Data!;
		Assert.Multiple(() =>
		{
			Assert.That(_harness.Cache.GetIconsByPackId(restoredPack.Id), Is.Empty);
			Assert.That(_harness.Cache.FindByMasterContentHash(MasterContentHash.Compute("tampered"u8)),
				Is.Null,
				"tampered bytes must not become a deduplication candidate either");
		});
	}

	[Test]
	public async Task RestoreAsNewPack_RecordsTheHashOfTheBytesItActuallyWrote()
	{
		var sourcePack = await _harness.CreatePack("Exported");
		await AddReadyIcon(sourcePack, "good", sizes: []);

		var result = await _harness.RestoreService.RestoreAsNewPack("Exported.macroDeckIconPack",
			new MemoryStream(await Export(sourcePack.Id)),
			CancellationToken.None);

		var restored = _harness.Cache.GetIconsByPackId(result.Data!.Id).Single();
		Assert.That(restored.MasterContentHash,
			Is.EqualTo(MasterContentHash.Compute("master:good"u8).Value));
	}

	private static string ReadZipEntryText(byte[] archive, string entryName)
	{
		using var zip = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read);
		using var stream = zip.GetEntry(entryName)!.Open();
		using var reader = new StreamReader(stream);
		return reader.ReadToEnd();
	}

	private static byte[] ReplaceZipEntry(byte[] archive, string suffix, byte[] replacement)
	{
		var entries = new List<(string Name, byte[] Content)>();
		using (var source = new ZipArchive(new MemoryStream(archive), ZipArchiveMode.Read))
		{
			foreach (var entry in source.Entries)
			{
				using var entryStream = entry.Open();
				using var buffer = new MemoryStream();
				entryStream.CopyTo(buffer);
				entries.Add((entry.FullName,
					entry.FullName.EndsWith(suffix, StringComparison.Ordinal) ? replacement : buffer.ToArray()));
			}
		}

		return CreateZip(entries.ToArray());
	}

	private async Task<byte[]> Export(Guid packId)
	{
		var stream = new MemoryStream();
		var result = await _harness.CreateExportService().Export(packId, stream, CancellationToken.None);
		Assert.That(result.Success, Is.True);
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
}
