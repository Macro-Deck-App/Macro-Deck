using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>What one completed host-to-plugin transfer delivered.</summary>
internal sealed record HostAssetTransfer(bool Accepted, string MimeType, byte[] Content);

/// <summary>
/// Reassembles a chunked <c>host.asset.begin</c>/<c>chunk</c>/<c>commit</c> transfer and hands the
/// bytes to whoever is waiting for that transfer id. The receiving mirror of
/// <see cref="PluginAssetUploader" />, down to the content-hash check on commit.
/// </summary>
internal interface IPluginHostAssetReceiver
{
	/// <summary>
	/// Waits for the transfer the host announced under <paramref name="transferId" />. Safe to call after
	/// the transfer has already completed: a finished transfer is held until it is claimed, because the
	/// reply naming the id and the transfer itself race over the same socket.
	/// </summary>
	Task<HostAssetTransfer> AwaitAsync(string transferId, CancellationToken cancellationToken);

	/// <summary>Handles one <c>host.asset.*</c> message; returns the ack to send back.</summary>
	ProtocolEnvelope Handle(ProtocolEnvelope envelope);

	/// <summary>Fails everything in flight - a transfer cannot outlive the connection it arrived on.</summary>
	void Reset();
}

internal sealed class PluginHostAssetReceiver : IPluginHostAssetReceiver
{
	private readonly Lock _gate = new();
	private readonly Dictionary<string, Transfer> _transfers = new(StringComparer.Ordinal);

	private readonly ConcurrentDictionary<string, TaskCompletionSource<HostAssetTransfer>> _completions =
		new(StringComparer.Ordinal);

	public async Task<HostAssetTransfer> AwaitAsync(string transferId, CancellationToken cancellationToken)
	{
		var completion = Completion(transferId);

		try
		{
			await using var registration = cancellationToken.Register(
				static state => ((TaskCompletionSource<HostAssetTransfer>)state!).TrySetCanceled(),
				completion).ConfigureAwait(false);

			return await completion.Task.ConfigureAwait(false);
		}
		finally
		{
			_completions.TryRemove(transferId, out _);
		}
	}

	public ProtocolEnvelope Handle(ProtocolEnvelope envelope)
	{
		ArgumentNullException.ThrowIfNull(envelope);

		var (assetId, index, accepted) = envelope.Type switch
		{
			MessageTypes.HostAssetBegin => Begin(Payload<HostAssetBeginPayload>(envelope)),
			MessageTypes.HostAssetChunk => Chunk(Payload<HostAssetChunkPayload>(envelope)),
			MessageTypes.HostAssetCommit => Commit(Payload<HostAssetCommitPayload>(envelope)),
			_ => (string.Empty, (int?)null, false)
		};

		return new ProtocolEnvelope
		{
			Type = MessageTypes.HostAssetAck,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = envelope.Id,
			Payload = JsonSerializer.SerializeToElement(
				new HostAssetAckPayload { AssetId = assetId, Index = index, Accepted = accepted },
				PluginProtocolJson.Options)
		};
	}

	public void Reset()
	{
		lock (_gate)
		{
			_transfers.Clear();
		}

		// Removed as well as failed: a waiter already holds its own reference, and a transfer nobody
		// claimed - a cancelled icon call, say - would otherwise be held for the life of the process.
		foreach (var transferId in _completions.Keys)
		{
			if (_completions.TryRemove(transferId, out var completion))
			{
				completion.TrySetResult(Failed);
			}
		}
	}

	private static readonly HostAssetTransfer Failed = new(false, string.Empty, []);

	private TaskCompletionSource<HostAssetTransfer> Completion(string transferId)
		=> _completions.GetOrAdd(transferId,
			static _ => new TaskCompletionSource<HostAssetTransfer>(TaskCreationOptions
				.RunContinuationsAsynchronously));

	private (string AssetId, int? Index, bool Accepted) Begin(HostAssetBeginPayload? payload)
	{
		if (payload is null ||
			string.IsNullOrEmpty(payload.AssetId) ||
			string.IsNullOrEmpty(payload.ContentHash) ||
			payload.TotalBytes < 0 ||
			payload.TotalBytes > ProtocolLimits.MaxAssetBytes)
		{
			return (payload?.AssetId ?? string.Empty, null, false);
		}

		lock (_gate)
		{
			_transfers[payload.AssetId] = new Transfer(payload.MimeType, payload.TotalBytes, payload.ContentHash);
		}

		return (payload.AssetId, null, true);
	}

	private (string AssetId, int? Index, bool Accepted) Chunk(HostAssetChunkPayload? payload)
	{
		if (payload is null)
		{
			return (string.Empty, null, false);
		}

		byte[] data;
		try
		{
			data = Convert.FromBase64String(payload.Data);
		}
		catch (FormatException)
		{
			return (payload.AssetId, payload.Index, false);
		}

		lock (_gate)
		{
			if (!_transfers.TryGetValue(payload.AssetId, out var transfer) ||
				payload.Index != transfer.NextIndex ||
				data.Length > ProtocolLimits.MaxAssetChunkBytes ||
				transfer.Buffer.Length + data.Length > transfer.TotalBytes)
			{
				return (payload.AssetId, payload.Index, false);
			}

			transfer.Buffer.Write(data);
			transfer.NextIndex++;
		}

		return (payload.AssetId, payload.Index, true);
	}

	private (string AssetId, int? Index, bool Accepted) Commit(HostAssetCommitPayload? payload)
	{
		if (payload is null)
		{
			return (string.Empty, null, false);
		}

		Transfer? transfer;
		lock (_gate)
		{
			if (!_transfers.Remove(payload.AssetId, out transfer))
			{
				return (payload.AssetId, null, false);
			}
		}

		var bytes = transfer.Buffer.ToArray();
		transfer.Buffer.Dispose();

		var accepted = bytes.Length == transfer.TotalBytes &&
			string.Equals(AssetContentHash.Compute(bytes), transfer.ContentHash, StringComparison.Ordinal);

		Completion(payload.AssetId)
			.TrySetResult(accepted ? new HostAssetTransfer(true, transfer.MimeType, bytes) : Failed);

		return (payload.AssetId, null, accepted);
	}

	private static T? Payload<T>(ProtocolEnvelope envelope)
		where T : class
	{
		try
		{
			return envelope.Payload?.Deserialize<T>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			return null;
		}
	}

	private sealed class Transfer(string mimeType, int totalBytes, string contentHash)
	{
		public string MimeType { get; } = mimeType;

		public int TotalBytes { get; } = totalBytes;

		public string ContentHash { get; } = contentHash;

		public int NextIndex { get; set; }

		public MemoryStream Buffer { get; } = new(totalBytes);
	}
}
