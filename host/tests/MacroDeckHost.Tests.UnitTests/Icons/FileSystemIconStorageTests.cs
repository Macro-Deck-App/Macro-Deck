using System.Text;

namespace MacroDeckHost.Tests.UnitTests.Icons;

[TestFixture]
public class FileSystemIconStorageTests
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
	public async Task StageOriginal_RoundTripsThroughOpenStagedOriginal()
	{
		var batchId = Guid.NewGuid();
		var iconId = Guid.NewGuid();
		var content = Encoding.UTF8.GetBytes("image-bytes");

		await _harness.Storage.StageOriginal(batchId,
			iconId,
			"photo.PNG",
			new MemoryStream(content),
			CancellationToken.None);

		await using var staged = _harness.Storage.OpenStagedOriginal(batchId, iconId);
		Assert.That(staged, Is.Not.Null);
		using var reader = new MemoryStream();
		await staged!.CopyToAsync(reader);
		Assert.That(reader.ToArray(), Is.EqualTo(content));
	}

	[Test]
	public void OpenStagedOriginal_MissingFile_ReturnsNull()
	{
		Assert.That(_harness.Storage.OpenStagedOriginal(Guid.NewGuid(), Guid.NewGuid()), Is.Null);
	}

	[Test]
	public async Task WriteVariant_WritesAtomicallyWithoutLeftoverTempFile()
	{
		var packId = Guid.NewGuid();
		var iconId = Guid.NewGuid();

		await _harness.Storage.WriteVariant(packId,
			iconId,
			"128",
			new byte[] { 9, 9, 9 },
			CancellationToken.None);

		await using var stream = _harness.Storage.OpenVariant(packId, iconId, "128");
		Assert.That(stream, Is.Not.Null);

		var iconDirectory = Path.Combine(_harness.Paths.IconPacksDirectory,
			packId.ToString(),
			"icons",
			iconId.ToString());
		Assert.That(Directory.EnumerateFiles(iconDirectory, "*.tmp"), Is.Empty);
	}

	[Test]
	public async Task DeleteIconFiles_RemovesTheIconFolder()
	{
		var packId = Guid.NewGuid();
		var iconId = Guid.NewGuid();
		await _harness.Storage.WriteVariant(packId, iconId, "master", new byte[] { 1 }, CancellationToken.None);

		_harness.Storage.DeleteIconFiles(packId, iconId);

		Assert.That(_harness.Storage.OpenVariant(packId, iconId, "master"), Is.Null);
	}

	[Test]
	public async Task CleanupBatchStaging_RemovesEverythingIncludingArchives()
	{
		var batchId = Guid.NewGuid();
		await _harness.Storage.StageOriginal(batchId,
			Guid.NewGuid(),
			"a.png",
			new MemoryStream([1]),
			CancellationToken.None);
		await _harness.Storage.StageArchive(batchId, "pack.zip", new MemoryStream([2]), CancellationToken.None);

		Assert.That(_harness.Storage.EnumerateStagedBatchIds(), Does.Contain(batchId));

		_harness.Storage.CleanupBatchStaging(batchId);

		Assert.That(_harness.Storage.EnumerateStagedBatchIds(), Does.Not.Contain(batchId));
	}

	[Test]
	public async Task GetStagedArchivePaths_ListsOnlyArchives()
	{
		var batchId = Guid.NewGuid();
		await _harness.Storage.StageOriginal(batchId,
			Guid.NewGuid(),
			"a.png",
			new MemoryStream([1]),
			CancellationToken.None);
		await _harness.Storage.StageArchive(batchId, "pack.zip", new MemoryStream([2]), CancellationToken.None);

		var archives = _harness.Storage.GetStagedArchivePaths(batchId);

		Assert.That(archives, Has.Count.EqualTo(1));
		Assert.That(Path.GetFileName(archives[0]), Does.EndWith("pack.zip"));
	}
}
