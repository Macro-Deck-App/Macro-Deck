using System.IO.Compression;
using System.Text;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeck.Plugin.Packaging.Manifest;

namespace MacroDeck.Plugin.Packaging.Tests.UnitTests.Artifacts;

[TestFixture]
internal sealed class PluginArtifactReaderTests
{
	private static readonly string[] _wellFormedFiles = ["manifest.json", "app"];

	private string _root = null!;
	private PluginArtifactReader _reader = null!;

	[SetUp]
	public void SetUp()
	{
		_root = Path.Combine(Path.GetTempPath(), "md-artifact-reader-tests-" + Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(_root);
		_reader = new PluginArtifactReader(new PluginManifestReader(), Serilog.Core.Logger.None);
	}

	[TearDown]
	public void TearDown()
	{
		if (Directory.Exists(_root))
		{
			Directory.Delete(_root, true);
		}
	}

	private static string ManifestJson()
	{
		return $$"""
				 {
				 	"manifestVersion": 1,
				 	"id": "com.example.reader",
				 	"name": "Example",
				 	"version": "1.0.0",
				 	"entrypoints": {
				 		"{{PluginRuntimeIdentifiers.Current}}": { "executable": "app" }
				 	}
				 }
				 """;
	}

	private string WriteArchive(Action<ZipArchive> fill)
	{
		var path = Path.Combine(_root, Guid.NewGuid().ToString("N") + ".macroDeckPlugin");
		using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
		{
			fill(archive);
		}

		return path;
	}

	private static void AddEntry(ZipArchive archive, string name, byte[] content, CompressionLevel level)
	{
		var entry = archive.CreateEntry(name, level);
		using var stream = entry.Open();
		stream.Write(content);
	}

	private static void AddManifest(ZipArchive archive)
	{
		AddEntry(archive,
			PluginArtifactFiles.ManifestFileName,
			Encoding.UTF8.GetBytes(ManifestJson()),
			CompressionLevel.NoCompression);
	}

	[Test]
	public async Task A_well_formed_archive_inspects_and_extracts()
	{
		var artifact = WriteArchive(archive =>
		{
			AddManifest(archive);
			AddEntry(archive, "app", Encoding.UTF8.GetBytes("binary"), CompressionLevel.Optimal);
		});
		var target = Path.Combine(_root, "extract");

		var inspected = await _reader.Inspect(artifact);
		var extracted = await _reader.ExtractTo(artifact, target);

		Assert.Multiple(() =>
		{
			Assert.That(inspected.Success, Is.True, inspected.ErrorMessage);
			Assert.That(inspected.Manifest!.Id, Is.EqualTo("com.example.reader"));
			Assert.That(extracted.Success, Is.True, extracted.ErrorMessage);
			Assert.That(extracted.ExtractedFiles, Is.EquivalentTo(_wellFormedFiles));
		});
	}

	[Test]
	public async Task A_file_that_is_not_a_zip_is_an_invalid_archive()
	{
		var artifact = Path.Combine(_root, "not-a-zip.macroDeckPlugin");
		await File.WriteAllTextAsync(artifact, "this is not a zip archive");

		var result = await _reader.Inspect(artifact);

		Assert.That(result.Error, Is.EqualTo(PluginInstallError.InvalidArchive));
	}

	[Test]
	public async Task An_archive_beyond_the_compression_ratio_limit_is_refused()
	{
		var payload = new byte[(PluginArtifactLimits.MaxCompressionRatio + 50) * 64 * 1024];
		var artifact = WriteArchive(archive =>
		{
			AddManifest(archive);
			AddEntry(archive, "app", payload, CompressionLevel.SmallestSize);
		});
		var target = Path.Combine(_root, "extract");

		var inspected = await _reader.Inspect(artifact);
		var extracted = await _reader.ExtractTo(artifact, target);

		Assert.Multiple(() =>
		{
			Assert.That(inspected.Error, Is.EqualTo(PluginInstallError.ArtifactLimitExceeded));
			Assert.That(extracted.Error, Is.EqualTo(PluginInstallError.ArtifactLimitExceeded));
			Assert.That(Directory.Exists(target), Is.False);
		});
	}

	[Test]
	public async Task An_archive_with_more_entries_than_the_limit_is_refused()
	{
		var artifact = WriteArchive(archive =>
		{
			AddManifest(archive);
			for (var i = 0; i < PluginArtifactLimits.MaxEntries; i++)
			{
				archive.CreateEntry($"f/{i}");
			}
		});

		var result = await _reader.Inspect(artifact);

		Assert.That(result.Error, Is.EqualTo(PluginInstallError.ArtifactLimitExceeded));
	}

	[TestCase("app", "app")]
	[TestCase("lib/plugin.dll", "LIB/Plugin.DLL")]
	public async Task A_duplicate_entry_is_refused_as_unsafe(string first, string second)
	{
		var artifact = WriteArchive(archive =>
		{
			AddManifest(archive);
			AddEntry(archive, first, Encoding.UTF8.GetBytes("one"), CompressionLevel.Optimal);
			AddEntry(archive, second, Encoding.UTF8.GetBytes("two"), CompressionLevel.Optimal);
		});
		var target = Path.Combine(_root, "extract");

		var inspected = await _reader.Inspect(artifact);
		var extracted = await _reader.ExtractTo(artifact, target);

		Assert.Multiple(() =>
		{
			Assert.That(inspected.Error, Is.EqualTo(PluginInstallError.UnsafeEntry));
			Assert.That(extracted.Error, Is.EqualTo(PluginInstallError.UnsafeEntry));
			Assert.That(Directory.Exists(target), Is.False);
		});
	}
}
