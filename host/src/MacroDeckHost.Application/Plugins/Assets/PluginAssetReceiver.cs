using System.Collections.Concurrent;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;

namespace MacroDeckHost.Application.Plugins.Assets;

public sealed class PluginAssetReceiver : IPluginAssetReceiver
{
	internal const int MaxInFlightTransfersPerPlugin = 4;

	internal const int MaxBufferedBytesPerPlugin = 2 * ProtocolLimits.MaxAssetBytes;

	private readonly ConcurrentDictionary<string, PluginTransfers> _byPlugin = new(StringComparer.Ordinal);
	private readonly IPluginAssetCache _cache;

	public PluginAssetReceiver(IPluginAssetCache cache) => _cache = cache;

	public event EventHandler<AssetCommittedEventArgs>? AssetCommitted;

	public AssetOperationResult Begin(string pluginId,
		string assetId,
		string kind,
		string mimeType,
		int totalBytes,
		string contentHash)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);

		if (string.IsNullOrEmpty(assetId) ||
			!AssetKinds.IsKnown(kind) ||
			string.IsNullOrEmpty(mimeType) ||
			string.IsNullOrEmpty(contentHash) ||
			totalBytes < 0)
		{
			return AssetOperationResult.Fail(ProtocolErrorCodes.InvalidPayload, "Malformed asset.begin.");
		}

		if (totalBytes > ProtocolLimits.MaxAssetBytes)
		{
			return AssetOperationResult.Fail(ProtocolErrorCodes.AssetTooLarge,
				$"Asset '{assetId}' declares {totalBytes} bytes, over the {ProtocolLimits.MaxAssetBytes} byte limit.");
		}

		var plugin = _byPlugin.GetOrAdd(pluginId, static _ => new PluginTransfers());

		lock (plugin.Gate)
		{
			if (plugin.Transfers.ContainsKey(assetId))
			{
				return AssetOperationResult.Fail(ProtocolErrorCodes.InvalidPayload,
					$"Asset '{assetId}' was already begun.");
			}

			if (plugin.Transfers.Count >= MaxInFlightTransfersPerPlugin)
			{
				return AssetOperationResult.Fail(ProtocolErrorCodes.QueueOverflow,
					$"Plugin '{pluginId}' already has {MaxInFlightTransfersPerPlugin} asset uploads in flight.");
			}

			if (plugin.BufferedBytes + totalBytes > MaxBufferedBytesPerPlugin)
			{
				return AssetOperationResult.Fail(ProtocolErrorCodes.QueueOverflow,
					$"Plugin '{pluginId}' has too many bytes buffered across in-flight asset uploads.");
			}

			plugin.Transfers[assetId] = new Transfer(kind, mimeType, totalBytes, contentHash);
			plugin.BufferedBytes += totalBytes;
			return AssetOperationResult.Ok();
		}
	}

	public AssetOperationResult Chunk(string pluginId, string assetId, int index, ReadOnlySpan<byte> data)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);

		if (!_byPlugin.TryGetValue(pluginId, out var plugin))
		{
			return UnknownAsset(assetId);
		}

		lock (plugin.Gate)
		{
			if (!plugin.Transfers.TryGetValue(assetId, out var transfer))
			{
				return UnknownAsset(assetId);
			}

			if (data.Length > ProtocolLimits.MaxAssetChunkBytes)
			{
				return AssetOperationResult.Fail(ProtocolErrorCodes.PayloadTooLarge,
					$"Chunk {index} of asset '{assetId}' is {data.Length} bytes, over the {ProtocolLimits.MaxAssetChunkBytes} byte chunk limit.");
			}

			if (index < transfer.NextIndex)
			{
				return AssetOperationResult.Fail(ProtocolErrorCodes.InvalidPayload,
					$"Chunk {index} of asset '{assetId}' is a duplicate; {transfer.NextIndex} was expected.");
			}

			if (index > transfer.NextIndex)
			{
				return AssetOperationResult.Fail(ProtocolErrorCodes.InvalidPayload,
					$"Chunk {index} of asset '{assetId}' arrived out of order; {transfer.NextIndex} was expected.");
			}

			if (transfer.Received + data.Length > transfer.TotalBytes)
			{
				return AssetOperationResult.Fail(ProtocolErrorCodes.PayloadTooLarge,
					$"Asset '{assetId}' has received more bytes than its asset.begin declared.");
			}

			transfer.Buffer.Write(data);
			transfer.Received += data.Length;
			transfer.NextIndex++;
			return AssetOperationResult.Ok();
		}
	}

	public AssetOperationResult Commit(string pluginId, string assetId)
	{
		ArgumentException.ThrowIfNullOrEmpty(pluginId);

		if (!_byPlugin.TryGetValue(pluginId, out var plugin))
		{
			return UnknownAsset(assetId);
		}

		byte[] bytes;
		string kind;
		string mimeType;
		string contentHash;

		lock (plugin.Gate)
		{
			if (!plugin.Transfers.TryGetValue(assetId, out var transfer))
			{
				return UnknownAsset(assetId);
			}

			if (transfer.Received != transfer.TotalBytes)
			{
				return AssetOperationResult.Fail(ProtocolErrorCodes.InvalidPayload,
					$"Asset '{assetId}' committed after {transfer.Received} of its declared {transfer.TotalBytes} bytes.");
			}

			bytes = transfer.Buffer.ToArray();
			var actualHash = AssetContentHash.Compute(bytes);

			if (!string.Equals(actualHash, transfer.ContentHash, StringComparison.Ordinal))
			{
				RemoveTransfer(plugin, assetId, transfer);
				return AssetOperationResult.Fail(ProtocolErrorCodes.InvalidPayload,
					$"Asset '{assetId}' failed its content hash check.");
			}

			kind = transfer.Kind;
			mimeType = transfer.MimeType;
			contentHash = actualHash;
			RemoveTransfer(plugin, assetId, transfer);
		}

		_cache.Write(contentHash, mimeType, bytes);
		AssetCommitted?.Invoke(this, new AssetCommittedEventArgs(pluginId, kind, contentHash, mimeType, bytes));

		return AssetOperationResult.Ok();
	}

	public void DropSession(string pluginId)
	{
		if (_byPlugin.TryRemove(pluginId, out var plugin))
		{
			lock (plugin.Gate)
			{
				plugin.Transfers.Clear();
				plugin.BufferedBytes = 0;
			}
		}
	}

	private static AssetOperationResult UnknownAsset(string assetId)
		=> AssetOperationResult.Fail(ProtocolErrorCodes.InvalidPayload, $"Unknown asset id '{assetId}'.");

	private static void RemoveTransfer(PluginTransfers plugin, string assetId, Transfer transfer)
	{
		plugin.Transfers.Remove(assetId, out _);
		plugin.BufferedBytes -= transfer.TotalBytes;
		transfer.Buffer.Dispose();
	}

	private sealed class PluginTransfers
	{
		public readonly Lock Gate = new();
		public readonly Dictionary<string, Transfer> Transfers = new(StringComparer.Ordinal);
		public int BufferedBytes;
	}

	private sealed class Transfer(string kind, string mimeType, int totalBytes, string contentHash)
	{
		public string Kind { get; } = kind;

		public string MimeType { get; } = mimeType;

		public int TotalBytes { get; } = totalBytes;

		public string ContentHash { get; } = contentHash;

		public int NextIndex { get; set; }

		public int Received { get; set; }

		public MemoryStream Buffer { get; } = new(totalBytes);
	}
}
