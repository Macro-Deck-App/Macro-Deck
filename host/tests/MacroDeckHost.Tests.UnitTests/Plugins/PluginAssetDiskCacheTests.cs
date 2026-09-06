using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Infrastructure.Plugins;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginAssetDiskCacheTests
{
	private TestPaths _paths = null!;
	private PluginAssetDiskCache _cache = null!;

	[SetUp]
	public void SetUp()
	{
		_paths = new TestPaths();
		_cache = new PluginAssetDiskCache(_paths, Serilog.Log.Logger);
	}

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void A_path_traversal_content_hash_is_never_read()
	{
		var outsideDirectory = Path.Combine(Path.GetTempPath(), "macro-deck-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(outsideDirectory);
		File.WriteAllBytes(Path.Combine(outsideDirectory, "secret.bin"), [1, 2, 3]);

		try
		{
			var traversal = "sha256:" + Path.Combine("..", "..", "..", Path.GetFileName(outsideDirectory), "secret");
			var found = _cache.TryRead(traversal, out var bytes, out var mimeType);

			Assert.Multiple(() =>
			{
				Assert.That(found, Is.False);
				Assert.That(bytes, Is.Empty);
				Assert.That(mimeType, Is.Empty);
			});
		}
		finally
		{
			Directory.Delete(outsideDirectory, recursive: true);
		}
	}

	[Test]
	public void An_absolute_path_content_hash_is_never_read()
	{
		var absolute = "sha256:" + Path.Combine(Path.GetTempPath(), "does-not-matter");
		var found = _cache.TryRead(absolute, out _, out _);

		Assert.That(found, Is.False);
	}

	[Test]
	public void A_malformed_content_hash_is_rejected_by_write_too()
	{
		// Defence in depth: even if a future caller skips validation before Write, the cache itself must
		// not persist something it cannot safely name a file after.
		_cache.Write("sha256:not-valid-hex", "image/png", [1, 2, 3]);

		var found = _cache.TryRead("sha256:not-valid-hex", out _, out _);

		Assert.That(found, Is.False);
	}

	[Test]
	public void A_well_formed_hash_round_trips()
	{
		var bytes = new byte[] { 9, 8, 7 };
		var hash = MacroDeck.Plugin.Protocol.Assets.AssetContentHash.Compute(bytes);

		_cache.Write(hash, "image/png", bytes);
		var found = _cache.TryRead(hash, out var readBytes, out var mimeType);

		Assert.Multiple(() =>
		{
			Assert.That(found, Is.True);
			Assert.That(readBytes, Is.EqualTo(bytes));
			Assert.That(mimeType, Is.EqualTo("image/png"));
		});
	}

	[Test]
	public void A_cached_file_larger_than_the_asset_limit_is_refused_on_read()
	{
		// Simulate a file that somehow grew past the upload-time limit (a corrupted or hand-placed file) -
		// TryRead's own size check must catch it even though nothing on this path could produce it via
		// Write, since Write always caps at ProtocolLimits.MaxAssetBytes through PluginAssetReceiver.
		var hash = "sha256:" + new string('a', 64);
		var (dataPath, _) = PathsForTest(hash);
		Directory.CreateDirectory(Path.GetDirectoryName(dataPath)!);
		using (var stream = File.Create(dataPath))
		{
			stream.SetLength(ProtocolLimits.MaxAssetBytes + 1);
		}

		var found = _cache.TryRead(hash, out var bytes, out _);

		Assert.Multiple(() =>
		{
			Assert.That(found, Is.False);
			Assert.That(bytes, Is.Empty);
		});
	}

	private (string DataPath, string MetaPath) PathsForTest(string contentHash)
	{
		var directory = Path.Combine(_paths.PluginsDirectory, "assets");
		var fileName = contentHash["sha256:".Length..];
		return (Path.Combine(directory, fileName + ".bin"), Path.Combine(directory, fileName + ".mime"));
	}
}

[TestFixture]
public class PluginAssetDiskCacheBoundsTests
{
	private TestPaths _paths = null!;

	[SetUp]
	public void SetUp() => _paths = new TestPaths();

	[TearDown]
	public void TearDown() => _paths.Cleanup();

	[Test]
	public void Writing_past_the_memory_bound_evicts_the_least_recently_used_entry()
	{
		var cache = new PluginAssetDiskCache(_paths, Serilog.Log.Logger, maxMemoryBytes: 20, maxDiskBytes: 1_000_000);

		var first = new byte[10];
		var second = new byte[10];
		var third = new byte[10];
		Array.Fill(first, (byte)1);
		Array.Fill(second, (byte)2);
		Array.Fill(third, (byte)3);

		var hashFirst = MacroDeck.Plugin.Protocol.Assets.AssetContentHash.Compute(first);
		var hashSecond = MacroDeck.Plugin.Protocol.Assets.AssetContentHash.Compute(second);
		var hashThird = MacroDeck.Plugin.Protocol.Assets.AssetContentHash.Compute(third);

		cache.Write(hashFirst, "application/octet-stream", first);
		cache.Write(hashSecond, "application/octet-stream", second);

		cache.Write(hashThird, "application/octet-stream", third);

		var foundFirst = cache.TryRead(hashFirst, out var firstBytes, out _);
		var foundSecond = cache.TryRead(hashSecond, out _, out _);
		var foundThird = cache.TryRead(hashThird, out _, out _);

		Assert.Multiple(() =>
		{
			Assert.That(foundFirst, Is.True, "an evicted entry must still be readable from disk");
			Assert.That(firstBytes, Is.EqualTo(first));
			Assert.That(foundSecond, Is.True);
			Assert.That(foundThird, Is.True);
		});
	}

	[Test]
	public void Committing_many_distinct_assets_keeps_the_on_disk_directory_bounded()
	{
		var cache = new PluginAssetDiskCache(_paths, Serilog.Log.Logger, maxMemoryBytes: 1_000_000, maxDiskBytes: 30);
		var assetsDirectory = Path.Combine(_paths.PluginsDirectory, "assets");

		for (var i = 0; i < 20; i++)
		{
			var bytes = new byte[10];
			Array.Fill(bytes, (byte)i);
			var hash = MacroDeck.Plugin.Protocol.Assets.AssetContentHash.Compute(bytes);
			cache.Write(hash, "application/octet-stream", bytes);
		}

		var totalBytesOnDisk = Directory.GetFiles(assetsDirectory, "*.bin").Sum(path => new FileInfo(path).Length);

		Assert.That(totalBytesOnDisk, Is.LessThanOrEqualTo(30));
	}
}
