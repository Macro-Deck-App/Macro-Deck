using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using MacroDeckHost.Application.Icons;
using MacroDeckHost.Application.Persistence.Icons;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using MacroDeckHost.Domain.Icons;
using MacroDeckHost.Infrastructure.Icons;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class IconPackExportServiceTests
{
	private static readonly string[] _expectedReadyNames = ["ready"];

	private static readonly JsonSerializerOptions _manifestOptions = new()
	{
		PropertyNameCaseInsensitive = true,
		Converters = { new JsonStringEnumConverter() }
	};

	private IconTestHarness _harness = null!;
	private IconPackExportService _service = null!;

	[SetUp]
	public void SetUp()
	{
		_harness = new IconTestHarness();
		_service = _harness.CreateExportService();
	}

	[TearDown]
	public void TearDown()
	{
		_harness.Dispose();
	}

	[Test]
	public async Task Export_WritesManifestAndAllVariantFiles()
	{
		var pack = await _harness.CreatePack("My Pack");
		var icon = await AddReadyIcon(pack, "star", sizes: [128]);

		await using var archive = await ExportToArchive(pack.Id);
		var manifest = ReadManifest(archive);

		Assert.Multiple(() =>
		{
			Assert.That(manifest.Name, Is.EqualTo("My Pack"));
			Assert.That(manifest.Icons, Has.Count.EqualTo(1));
			Assert.That(manifest.Icons[0].Id, Is.EqualTo(icon.Id));
			Assert.That(archive.GetEntry($"icons/{icon.Id}/master.webp"), Is.Not.Null);
			Assert.That(archive.GetEntry($"icons/{icon.Id}/128.webp"), Is.Not.Null);
		});
	}

	[Test]
	public async Task Export_ScrubsDefaultReadOnlySourceIdAndBatchIds()
	{
		var pack = await _harness.CreatePack("Default Pack", isDefault: true);
		pack.SourceId = "import:someorigin";
		await _harness.Cache.AddOrUpdatePack(pack);
		var icon = await AddReadyIcon(pack, "a", sizes: [], importBatchId: Guid.NewGuid());

		await using var archive = await ExportToArchive(pack.Id);
		var manifest = ReadManifest(archive);

		Assert.Multiple(() =>
		{
			Assert.That(manifest.IsDefault, Is.False);
			Assert.That(manifest.IsReadOnly, Is.False);
			Assert.That(manifest.SourceId, Is.Null);
			Assert.That(manifest.Icons.Single(i => i.Id == icon.Id).ImportBatchId, Is.Null);
		});
	}

	[Test]
	public async Task Export_ExcludesIconsThatAreNotReady()
	{
		var pack = await _harness.CreatePack();
		await AddReadyIcon(pack, "ready", sizes: []);
		await _harness.Cache.AddIcons(pack.Id,
		[
			new IconEntity
			{
				Id = Guid.CreateVersion7(),
				PackId = pack.Id,
				Name = "pending",
				ProcessingState = IconProcessingState.Pending,
				CreatedAt = DateTime.UtcNow
			}
		]);

		await using var archive = await ExportToArchive(pack.Id);
		var manifest = ReadManifest(archive);

		Assert.That(manifest.Icons.Select(i => i.Name), Is.EqualTo(_expectedReadyNames));
	}

	[Test]
	public async Task Export_DeclaresEveryEntryWithItsRealDigestAndSize()
	{
		var pack = await _harness.CreatePack("My Pack");
		var icon = await AddReadyIcon(pack, "star", sizes: [128]);

		await using var archive = await ExportToArchive(pack.Id);
		var manifest = ReadManifest(archive);

		Assert.That(manifest.Files, Is.Not.Null);
		var declaredPaths = manifest.Files!.Select(f => f.Path).ToList();
		Assert.That(declaredPaths,
			Is.EquivalentTo(new[] { $"icons/{icon.Id}/master.webp", $"icons/{icon.Id}/128.webp" }));

		foreach (var file in manifest.Files!)
		{
			var entry = archive.GetEntry(file.Path);
			Assert.That(entry, Is.Not.Null);
			using var stream = entry!.Open();
			using var buffer = new MemoryStream();
			stream.CopyTo(buffer);
			var bytes = buffer.ToArray();

			Assert.That(file.Size, Is.EqualTo(bytes.Length));
			Assert.That(file.Sha256, Is.EqualTo("sha256:" + Convert.ToHexStringLower(SHA256.HashData(bytes))));
		}
	}

	[Test]
	public async Task Export_DeclaredFilesExcludeTheManifestItself()
	{
		var pack = await _harness.CreatePack("My Pack");
		await AddReadyIcon(pack, "star", sizes: []);

		await using var archive = await ExportToArchive(pack.Id);
		var manifest = ReadManifest(archive);

		Assert.That(manifest.Files!.Select(f => f.Path), Does.Not.Contain("pack.json"));
	}

	[Test]
	public async Task Export_TwiceForIdenticalContent_ProducesAnIdenticallyOrderedDeclaredList()
	{
		var pack = await _harness.CreatePack("My Pack");
		await AddReadyIcon(pack, "a", sizes: [128, 256]);
		await AddReadyIcon(pack, "b", sizes: [16]);

		await using var first = await ExportToArchive(pack.Id);
		await using var second = await ExportToArchive(pack.Id);

		var firstFiles = ReadManifest(first).Files!;
		var secondFiles = ReadManifest(second).Files!;

		Assert.Multiple(() =>
		{
			Assert.That(secondFiles.Select(f => f.Path),
				Is.EqualTo(firstFiles.Select(f => f.Path)),
				"declared order must be deterministic");
			Assert.That(firstFiles.Select(f => f.Path),
				Is.Ordered.Using<string>(StringComparer.Ordinal),
				"the declared list is sorted ordinally by path");
		});
	}

	[Test]
	public void Export_UnknownPack_Fails()
	{
		Assert.Multiple(() =>
		{
			Assert.That(_service.GetExportFileName(Guid.NewGuid()).Error, Is.EqualTo(IconPackError.NotFound));
			Assert.That(_service.Export(Guid.NewGuid(), Stream.Null, CancellationToken.None).Result.Error,
				Is.EqualTo(IconPackError.NotFound));
		});
	}

	[Test]
	public async Task GetExportFileName_SanitizesInvalidCharacters()
	{
		var pack = await _harness.CreatePack("My/Pack:2?");

		var fileName = _service.GetExportFileName(pack.Id);

		Assert.That(fileName.Data, Is.EqualTo("MyPack2.macroDeckIconPack"));
	}

	private async Task<IconEntity> AddReadyIcon(IconPackEntity pack,
		string name,
		int[] sizes,
		Guid? importBatchId = null)
	{
		var icon = new IconEntity
		{
			Id = Guid.CreateVersion7(),
			PackId = pack.Id,
			Name = name,
			ProcessingState = IconProcessingState.Ready,
			AvailableSizes = sizes,
			MasterContentHash = MasterContentHash.Compute("abc"u8).Value,
			ImportBatchId = importBatchId,
			CreatedAt = DateTime.UtcNow
		};
		await _harness.Cache.AddIcons(pack.Id, [icon]);
		await _harness.Storage.WriteVariant(pack.Id,
			icon.Id,
			IconVariants.Master,
			new byte[] { 1, 2, 3 },
			CancellationToken.None);
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

	private async Task<ZipArchive> ExportToArchive(Guid packId)
	{
		var stream = new MemoryStream();
		var result = await _service.Export(packId, stream, CancellationToken.None);
		Assert.That(result.Success, Is.True);
		return new ZipArchive(new MemoryStream(stream.ToArray()), ZipArchiveMode.Read);
	}

	private static IconPackManifest ReadManifest(ZipArchive archive)
	{
		var entry = archive.GetEntry("pack.json");
		Assert.That(entry, Is.Not.Null);
		using var stream = entry!.Open();
		var manifest = JsonSerializer.Deserialize<IconPackManifest>(stream, _manifestOptions);
		Assert.That(manifest, Is.Not.Null);
		return manifest!;
	}
}
