using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Tests.UnitTests.TestSupport;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginAssetReceiverTests
{
	private const string PluginId = "com.example.plugin";

	private InMemoryPluginAssetCache _cache = null!;
	private PluginAssetReceiver _receiver = null!;

	[SetUp]
	public void SetUp()
	{
		_cache = new InMemoryPluginAssetCache();
		_receiver = new PluginAssetReceiver(_cache);
	}

	[Test]
	public void A_transfer_declaring_more_than_the_asset_size_limit_is_rejected_at_begin()
	{
		var result = _receiver.Begin(PluginId,
			"a1",
			AssetKinds.Icon,
			"image/png",
			ProtocolLimits.MaxAssetBytes + 1,
			"sha256:whatever");

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.AssetTooLarge));
		});
	}

	[Test]
	public void A_chunk_over_the_chunk_size_limit_is_rejected()
	{
		Begin("a1", totalBytes: ProtocolLimits.MaxAssetChunkBytes + 1);

		var oversizedChunk = new byte[ProtocolLimits.MaxAssetChunkBytes + 1];
		var result = _receiver.Chunk(PluginId, "a1", 0, oversizedChunk);

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.PayloadTooLarge));
		});
	}

	[Test]
	public void Actual_bytes_exceeding_the_declared_total_are_rejected()
	{
		Begin("a1", totalBytes: 4);

		var result = _receiver.Chunk(PluginId, "a1", 0, new byte[8]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.PayloadTooLarge));
		});
	}

	[Test]
	public void An_out_of_order_chunk_index_is_rejected()
	{
		Begin("a1", totalBytes: 20);
		_receiver.Chunk(PluginId, "a1", 0, new byte[10]);

		var result = _receiver.Chunk(PluginId, "a1", 2, new byte[10]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
		});
	}

	[Test]
	public void A_duplicate_chunk_index_is_rejected()
	{
		Begin("a1", totalBytes: 20);
		_receiver.Chunk(PluginId, "a1", 0, new byte[10]);

		var result = _receiver.Chunk(PluginId, "a1", 0, new byte[10]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
		});
	}

	[Test]
	public void A_content_hash_mismatch_at_commit_is_rejected_and_nothing_is_cached()
	{
		var bytes = new byte[] { 1, 2, 3, 4 };
		Begin("a1",
			totalBytes: bytes.Length,
			contentHash: "sha256:0000000000000000000000000000000000000000000000000000000000000000");
		_receiver.Chunk(PluginId, "a1", 0, bytes);

		var result = _receiver.Commit(PluginId, "a1");

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
			Assert.That(_cache.TryRead(AssetContentHash.Compute(bytes), out _, out _), Is.False);
		});
	}

	[Test]
	public void An_unknown_asset_id_in_a_chunk_is_an_error_not_a_silent_no_op()
	{
		var result = _receiver.Chunk(PluginId, "never-begun", 0, new byte[4]);

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
		});
	}

	[Test]
	public void An_unknown_asset_id_in_a_commit_is_an_error_not_a_silent_no_op()
	{
		var result = _receiver.Commit(PluginId, "never-begun");

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
		});
	}

	[Test]
	public void More_than_the_per_plugin_transfer_limit_in_flight_is_rejected()
	{
		for (var i = 0; i < PluginAssetReceiver.MaxInFlightTransfersPerPlugin; i++)
		{
			var begun = Begin($"a{i}", totalBytes: 1);
			Assert.That(begun.Accepted, Is.True, $"setup begin {i} should have been accepted");
		}

		var result = Begin("one-too-many", totalBytes: 1);

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.QueueOverflow));
		});
	}

	[Test]
	public void More_buffered_bytes_than_the_per_plugin_byte_budget_is_rejected()
	{
		// Two transfers at the max asset size already reach the per-plugin byte budget
		// (PluginAssetReceiver.MaxBufferedBytesPerPlugin is deliberately smaller than
		// MaxInFlightTransfersPerPlugin * MaxAssetBytes - see its remarks), so a third of any size
		// overflows it well before the in-flight-transfer-count bound would ever apply.
		Begin("a1", totalBytes: ProtocolLimits.MaxAssetBytes);
		Begin("a2", totalBytes: ProtocolLimits.MaxAssetBytes);

		var result = Begin("a3", totalBytes: 1);

		Assert.Multiple(() =>
		{
			Assert.That(result.Accepted, Is.False);
			Assert.That(result.ErrorCode, Is.EqualTo(ProtocolErrorCodes.QueueOverflow));
		});
	}

	[Test]
	public void Dropping_a_session_frees_its_incomplete_transfers_and_their_buffered_bytes()
	{
		Begin("a1", totalBytes: ProtocolLimits.MaxAssetBytes);
		Begin("a2", totalBytes: ProtocolLimits.MaxAssetBytes);

		Assert.That(Begin("a3", totalBytes: 1).Accepted, Is.False);

		_receiver.DropSession(PluginId);

		var result = Begin("a4", totalBytes: ProtocolLimits.MaxAssetBytes);

		Assert.That(result.Accepted, Is.True);
	}

	[Test]
	public void Dropping_a_session_makes_its_asset_ids_unknown_again()
	{
		Begin("a1", totalBytes: 10);
		_receiver.DropSession(PluginId);

		var result = _receiver.Commit(PluginId, "a1");

		Assert.That(result.Accepted, Is.False);
	}

	[Test]
	public void A_complete_upload_is_verified_cached_and_raises_asset_committed()
	{
		var bytes = new byte[130 * 1024];
		Random.Shared.NextBytes(bytes);
		var contentHash = AssetContentHash.Compute(bytes);

		AssetCommittedEventArgs? raised = null;
		_receiver.AssetCommitted += (_, e) => raised = e;

		Begin("a1", totalBytes: bytes.Length, contentHash: contentHash, kind: AssetKinds.Icon, mimeType: "image/png");

		var offset = 0;
		var index = 0;
		while (offset < bytes.Length)
		{
			var length = Math.Min(ProtocolLimits.MaxAssetChunkBytes, bytes.Length - offset);
			var accepted = _receiver.Chunk(PluginId, "a1", index, bytes.AsSpan(offset, length));
			Assert.That(accepted.Accepted, Is.True, $"chunk {index} should have been accepted");
			offset += length;
			index++;
		}

		var commit = _receiver.Commit(PluginId, "a1");

		Assert.Multiple(() =>
		{
			Assert.That(commit.Accepted, Is.True);
			Assert.That(raised, Is.Not.Null);
			Assert.That(raised!.PluginId, Is.EqualTo(PluginId));
			Assert.That(raised.Kind, Is.EqualTo(AssetKinds.Icon));
			Assert.That(raised.ContentHash, Is.EqualTo(contentHash));
			Assert.That(raised.Bytes, Is.EqualTo(bytes));
			Assert.That(_cache.TryRead(contentHash, out var cachedBytes, out var cachedMimeType), Is.True);
			Assert.That(cachedBytes, Is.EqualTo(bytes));
			Assert.That(cachedMimeType, Is.EqualTo("image/png"));
		});
	}

	private AssetOperationResult Begin(
		string assetId,
		int totalBytes,
		string? contentHash = null,
		string kind = AssetKinds.Icon,
		string mimeType = "image/png")
		=> _receiver.Begin(PluginId,
			assetId,
			kind,
			mimeType,
			totalBytes,
			contentHash ?? AssetContentHash.Compute(new byte[totalBytes]));
}
