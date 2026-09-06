using System.Security.Cryptography;
using MacroDeck.Plugin.Packaging.Artifacts;
using MacroDeckHost.Application.Plugins.Installation;
using MacroDeckHost.Infrastructure.Plugins.Installation;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins.Installation;

[TestFixture]
internal sealed class PluginArtifactCacheTests
{
	private TestPaths _paths = null!;
	private string _sourceDirectory = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		Directory.CreateDirectory(_paths.PluginCacheDirectory);

		_sourceDirectory = Path.Combine(_paths.BaseDirectory, "sources");
		Directory.CreateDirectory(_sourceDirectory);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	private PluginArtifactCache CreateCache(long cacheBudgetBytes = 512L * 1024 * 1024)
	{
		var options = PluginInstallerOptions.Default with { CacheBudgetBytes = cacheBudgetBytes };
		return new PluginArtifactCache(_paths, options, Serilog.Core.Logger.None);
	}

	private string WriteSourceFile(byte[] content, string fileName)
	{
		var path = Path.Combine(_sourceDirectory, fileName);
		File.WriteAllBytes(path, content);
		return path;
	}

	private static string HexOf(byte[] content) => Convert.ToHexStringLower(SHA256.HashData(content));

	[Test]
	public async Task A_put_artifact_is_retrievable_and_the_returned_file_really_hashes_to_the_key()
	{
		var cache = CreateCache();
		var content = "artifact-bytes"u8.ToArray();
		var hex = HexOf(content);
		var source = WriteSourceFile(content, "a.macroDeckPlugin");

		await cache.Put(source, "sha256:" + hex);

		var found = cache.TryGet(hex, out var cachedPath);

		Assert.Multiple(() =>
		{
			Assert.That(found, Is.True);
			Assert.That(cachedPath, Is.Not.Null);
			Assert.That(HexOf(File.ReadAllBytes(cachedPath!)), Is.EqualTo(hex));
		});
	}

	[Test]
	public async Task TryGet_accepts_the_key_with_or_without_the_prefix_regardless_of_case()
	{
		var cache = CreateCache();
		var content = "abc"u8.ToArray();
		var hex = HexOf(content);
		await cache.Put(WriteSourceFile(content, "a.macroDeckPlugin"), "sha256:" + hex);

		Assert.Multiple(() =>
		{
			Assert.That(cache.TryGet(hex, out _), Is.True, "bare hex should hit");
			Assert.That(cache.TryGet("sha256:" + hex, out _), Is.True, "prefixed hex should hit");
			Assert.That(cache.TryGet(hex.ToUpperInvariant(), out _), Is.True, "upper-case hex should hit");
			Assert.That(cache.TryGet("sha256:" + hex.ToUpperInvariant(), out _),
				Is.True,
				"prefixed upper-case hex should hit");
		});
	}

	[Test]
	public void TryGet_returns_false_and_no_path_for_a_key_never_put()
	{
		var cache = CreateCache();
		var unknown = new string('0', 64);

		var found = cache.TryGet(unknown, out var cachedPath);

		Assert.Multiple(() =>
		{
			Assert.That(found, Is.False);
			Assert.That(cachedPath, Is.Null);
		});
	}

	[Test]
	public async Task An_index_entry_whose_backing_file_was_deleted_is_reported_as_a_miss()
	{
		var cache = CreateCache();
		var content = "goes-missing"u8.ToArray();
		var hex = HexOf(content);
		await cache.Put(WriteSourceFile(content, "a.macroDeckPlugin"), "sha256:" + hex);

		cache.TryGet(hex, out var cachedPath);
		File.Delete(cachedPath!);

		var found = cache.TryGet(hex, out var cachedPathAfterDeletion);

		Assert.Multiple(() =>
		{
			Assert.That(found, Is.False);
			Assert.That(cachedPathAfterDeletion, Is.Null);
		});
	}

	[Test]
	public async Task Prune_deletes_files_the_index_does_not_know_about()
	{
		var cache = CreateCache();
		var knownContent = "known"u8.ToArray();
		var knownHex = HexOf(knownContent);
		await cache.Put(WriteSourceFile(knownContent, "known.macroDeckPlugin"), "sha256:" + knownHex);

		var orphanHex = HexOf("orphan"u8.ToArray());
		var orphanPath = Path.Combine(_paths.PluginCacheDirectory,
			orphanHex + PluginArtifactFiles.MacroDeckPluginExtension);
		File.WriteAllText(orphanPath, "orphan");

		cache.Prune();

		Assert.Multiple(() =>
		{
			Assert.That(File.Exists(orphanPath), Is.False);
			Assert.That(cache.TryGet(knownHex, out _), Is.True, "the tracked entry must survive pruning");
		});
	}

	[Test]
	public async Task Lru_eviction_evicts_the_oldest_entries_and_keeps_the_newest_retrievable()
	{
		const int entrySize = 50;
		const long budget = 60L;
		var cache = CreateCache(budget);

		var hexA = await PutRandomArtifact(cache, entrySize, "a");
		await Task.Delay(15);
		var hexB = await PutRandomArtifact(cache, entrySize, "b");
		await Task.Delay(15);
		var hexC = await PutRandomArtifact(cache, entrySize, "c");

		var totalBytesOnDisk = Directory
			.EnumerateFiles(_paths.PluginCacheDirectory, "*" + PluginArtifactFiles.MacroDeckPluginExtension)
			.Sum(path => new FileInfo(path).Length);

		Assert.Multiple(() =>
		{
			Assert.That(totalBytesOnDisk, Is.LessThanOrEqualTo(budget));
			Assert.That(cache.TryGet(hexA, out _), Is.False, "the oldest entry must be evicted first");
			Assert.That(cache.TryGet(hexB, out _), Is.False, "the second-oldest entry must also be evicted");
			Assert.That(cache.TryGet(hexC, out _),
				Is.True,
				"the most recently added entry must still be retrievable");
		});
	}

	private async Task<string> PutRandomArtifact(PluginArtifactCache cache, int size, string fileNameSeed)
	{
		var content = new byte[size];
		RandomNumberGenerator.Fill(content);
		var hex = HexOf(content);
		var source = WriteSourceFile(content, $"{fileNameSeed}.macroDeckPlugin");
		await cache.Put(source, "sha256:" + hex);
		return hex;
	}

	[Test]
	public async Task Clear_empties_the_cache_but_leaves_the_directory_in_place()
	{
		var cache = CreateCache();
		var content = "content"u8.ToArray();
		var hex = HexOf(content);
		await cache.Put(WriteSourceFile(content, "a.macroDeckPlugin"), "sha256:" + hex);

		cache.Clear();

		Assert.Multiple(() =>
		{
			Assert.That(Directory.Exists(_paths.PluginCacheDirectory), Is.True);
			Assert.That(Directory.EnumerateFiles(_paths.PluginCacheDirectory,
					"*" + PluginArtifactFiles.MacroDeckPluginExtension),
				Is.Empty);
			Assert.That(cache.TryGet(hex, out _), Is.False);
		});
	}

	[TestCase("../escape")]
	[TestCase("nothex")]
	public void TryGet_refuses_a_key_that_is_not_a_64_character_hex_digest(string key)
	{
		var cache = CreateCache();

		Assert.Throws<ArgumentException>(() => cache.TryGet(key, out _));
	}
}
