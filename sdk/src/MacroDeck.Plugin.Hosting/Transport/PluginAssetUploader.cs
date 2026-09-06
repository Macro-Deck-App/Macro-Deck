using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Logging;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>
/// Drives <c>asset.begin</c> -&gt; one or more <c>asset.chunk</c> -&gt; <c>asset.commit</c>, awaiting the
/// host's <c>asset.ack</c> after every step before sending the next - the SDK never has more than one
/// unacknowledged step in flight for a given upload, so the host's receiver never has to reorder
/// anything. Registered as a singleton, like <see cref="HostInvoker" />, and sends through whichever
/// connection is current rather than holding one itself.
/// </summary>
internal sealed class PluginAssetUploader(PluginConnectionState state, TimeProvider timeProvider, ILogger logger)
	: IPluginAssetUploader
{
	private readonly ILogger _logger = logger.ForContext<PluginAssetUploader>();

	private readonly ConcurrentDictionary<string, TaskCompletionSource<AssetAckPayload>> _pending
		= new(StringComparer.Ordinal);

	public async Task<string> UploadAsync(string kind,
		string mimeType,
		byte[] data,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(kind);
		ArgumentException.ThrowIfNullOrEmpty(mimeType);
		ArgumentNullException.ThrowIfNull(data);

		if (data.Length > ProtocolLimits.MaxAssetBytes)
		{
			throw new AssetUploadException(
				$"An asset of kind '{kind}' is {data.Length} bytes, over the {ProtocolLimits.MaxAssetBytes} byte limit.");
		}

		var connection = state.ActiveConnection ??
			throw new AssetUploadException("There is no connection to the host.");

		var assetId = Guid.CreateVersion7().ToString();
		var contentHash = AssetContentHash.Compute(data);

		// One bounded overall budget for begin+every chunk+commit combined, not one budget per step -
		// see the interface's doc comment. Both the upload itself and the timeout race off the same
		// linked token, so cancelling it (below) stops whichever of the two is still pending.
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var uploadTask = RunUploadAsync(connection, assetId, kind, mimeType, data, contentHash, linked.Token);
		var timeoutTask = Task.Delay(ProtocolTimeouts.AssetUpload, timeProvider, linked.Token);

		var winner = await Task.WhenAny(uploadTask, timeoutTask).ConfigureAwait(false);
		await linked.CancelAsync().ConfigureAwait(false);

		if (winner == uploadTask)
		{
			return await uploadTask.ConfigureAwait(false);
		}

		// The timeout task won the race - either the asset upload budget genuinely elapsed, or the
		// caller's own token fired first and happened to be observed there instead of in uploadTask.
		// Only the former is this uploader's own failure to report.
		cancellationToken.ThrowIfCancellationRequested();

		throw new AssetUploadException($"Uploading asset '{assetId}' of kind '{kind}' timed out.");
	}

	public bool TryComplete(ProtocolEnvelope ack)
	{
		var correlationId = ack.CorrelationId;
		if (string.IsNullOrEmpty(correlationId) || !_pending.TryRemove(correlationId, out var pending))
		{
			return false;
		}

		var payload = ack.Payload?.Deserialize<AssetAckPayload>(PluginProtocolJson.Options);
		pending.TrySetResult(payload ?? new AssetAckPayload { AssetId = string.Empty, Accepted = false });
		return true;
	}

	private async Task<string> RunUploadAsync(
		PluginSessionConnection connection,
		string assetId,
		string kind,
		string mimeType,
		byte[] data,
		string contentHash,
		CancellationToken cancellationToken)
	{
		await SendAndAwaitAckAsync(connection,
				assetId,
				"begin",
				new ProtocolEnvelope
				{
					Type = MessageTypes.AssetBegin,
					Id = NewId(),
					Payload = Serialize(new AssetBeginPayload
					{
						AssetId = assetId, Kind = kind, MimeType = mimeType, TotalBytes = data.Length,
						ContentHash = contentHash
					})
				},
				cancellationToken)
			.ConfigureAwait(false);

		var offset = 0;
		var index = 0;

		// Still runs zero times for an empty asset - commit alone is enough to finish it.
		while (offset < data.Length)
		{
			var length = Math.Min(ProtocolLimits.MaxAssetChunkBytes, data.Length - offset);
			var chunk = data.AsSpan(offset, length).ToArray();

			await SendAndAwaitAckAsync(connection,
					assetId,
					$"chunk[{index}]",
					new ProtocolEnvelope
					{
						Type = MessageTypes.AssetChunk,
						Id = NewId(),
						Payload = Serialize(new AssetChunkPayload
							{ AssetId = assetId, Index = index, Data = Convert.ToBase64String(chunk) })
					},
					cancellationToken)
				.ConfigureAwait(false);

			offset += length;
			index++;
		}

		await SendAndAwaitAckAsync(connection,
				assetId,
				"commit",
				new ProtocolEnvelope
				{
					Type = MessageTypes.AssetCommit, Id = NewId(),
					Payload = Serialize(new AssetCommitPayload { AssetId = assetId })
				},
				cancellationToken)
			.ConfigureAwait(false);

		return contentHash;
	}

	/// <summary>Sends one <c>asset.*</c> step and waits for its <c>asset.ack</c>, keyed by this
	/// envelope's own id - the same correlation-by-request-id convention <see cref="HostInvoker" /> uses
	/// for <c>host.invoke</c>/<c>host.result</c>.</summary>
	private async Task SendAndAwaitAckAsync(
		PluginSessionConnection connection,
		string assetId,
		string step,
		ProtocolEnvelope envelope,
		CancellationToken cancellationToken)
	{
		var completion = new TaskCompletionSource<AssetAckPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[envelope.Id] = completion;

		try
		{
			await connection.SendAsync(envelope, cancellationToken).ConfigureAwait(false);

			using var registration = cancellationToken.Register(
				static state => ((TaskCompletionSource<AssetAckPayload>)state!).TrySetCanceled(),
				completion);

			var ack = await completion.Task.ConfigureAwait(false);

			if (!ack.Accepted)
			{
				_logger.AssetUploadRejected(assetId, step);
				throw new AssetUploadException($"The host rejected asset '{assetId}' at step '{step}'.");
			}
		}
		finally
		{
			_pending.TryRemove(envelope.Id, out _);
		}
	}

	private static string NewId() => Guid.CreateVersion7().ToString();

	private static JsonElement Serialize<T>(T value) =>
		JsonSerializer.SerializeToElement(value, PluginProtocolJson.Options);
}
