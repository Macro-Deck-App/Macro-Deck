using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Plugins.Assets;

/// <summary>
/// The host-to-plugin half of the chunked asset channel: <c>host.asset.begin</c> -&gt; one or more
/// <c>host.asset.chunk</c> -&gt; <c>host.asset.commit</c>, each step awaiting its <c>host.asset.ack</c>
/// before the next is sent. Exists because a protocol message is capped at
/// <see cref="ProtocolLimits.MaxMessageBytes" /> while the payloads it carries - icon bytes - are not.
/// </summary>
public interface IPluginHostAssetSender
{
	/// <summary>
	/// Streams one asset to a plugin under <paramref name="transferId" />, the id the plugin was told to
	/// expect in the reply that announced the transfer. False when the plugin rejected a step, the link
	/// went away, or the transfer ran out of time - never an exception, because a failed icon transfer
	/// must not take down whatever host call started it.
	/// </summary>
	Task<bool> SendAsync(
		string pluginId,
		string transferId,
		string kind,
		string mimeType,
		ReadOnlyMemory<byte> content,
		string contentHash,
		CancellationToken cancellationToken);

	bool TryComplete(string pluginId, ProtocolEnvelope ack);

	void DropSession(string pluginId);
}

public sealed class PluginHostAssetSender : IPluginHostAssetSender
{
	private readonly IPluginSessionRegistry _sessions;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<(string PluginId, string EnvelopeId),
		TaskCompletionSource<HostAssetAckPayload>> _pending = new();

	public PluginHostAssetSender(IPluginSessionRegistry sessions, TimeProvider timeProvider, ILogger logger)
	{
		_sessions = sessions;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<PluginHostAssetSender>();
	}

	public async Task<bool> SendAsync(
		string pluginId,
		string transferId,
		string kind,
		string mimeType,
		ReadOnlyMemory<byte> content,
		string contentHash,
		CancellationToken cancellationToken)
	{
		if (content.Length > ProtocolLimits.MaxAssetBytes)
		{
			_logger.Warning(
				"Asset transfer {TransferId} to plugin {PluginId} is {Bytes} bytes, over the {Limit} byte limit",
				transferId,
				pluginId,
				content.Length,
				ProtocolLimits.MaxAssetBytes);
			return false;
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		var transfer = RunAsync(pluginId, transferId, kind, mimeType, content, contentHash, linked.Token);
		var timeout = Task.Delay(ProtocolTimeouts.AssetUpload, _timeProvider, linked.Token);

		var winner = await Task.WhenAny(transfer, timeout).ConfigureAwait(false);
		await linked.CancelAsync().ConfigureAwait(false);

		if (winner != transfer)
		{
			_logger.Warning("Asset transfer {TransferId} to plugin {PluginId} timed out", transferId, pluginId);
			return false;
		}

		try
		{
			return await transfer.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Error(exception,
				"Asset transfer {TransferId} to plugin {PluginId} failed",
				transferId,
				pluginId);
			return false;
		}
	}

	public bool TryComplete(string pluginId, ProtocolEnvelope ack)
	{
		ArgumentNullException.ThrowIfNull(ack);

		if (ack.CorrelationId is not { Length: > 0 } correlationId ||
			!_pending.TryRemove((pluginId, correlationId), out var pending))
		{
			return false;
		}

		HostAssetAckPayload? payload;
		try
		{
			payload = ack.Payload?.Deserialize<HostAssetAckPayload>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			payload = null;
		}

		pending.TrySetResult(payload ?? new HostAssetAckPayload { AssetId = string.Empty, Accepted = false });
		return true;
	}

	public void DropSession(string pluginId)
	{
		foreach (var key in _pending.Keys.Where(key =>
			string.Equals(key.PluginId, pluginId, StringComparison.Ordinal)))
		{
			if (_pending.TryRemove(key, out var pending))
			{
				pending.TrySetResult(new HostAssetAckPayload { AssetId = string.Empty, Accepted = false });
			}
		}
	}

	private async Task<bool> RunAsync(
		string pluginId,
		string transferId,
		string kind,
		string mimeType,
		ReadOnlyMemory<byte> content,
		string contentHash,
		CancellationToken cancellationToken)
	{
		if (!await StepAsync(pluginId,
				new HostAssetBeginPayload
				{
					AssetId = transferId,
					Kind = kind,
					MimeType = mimeType,
					TotalBytes = content.Length,
					ContentHash = contentHash
				},
				MessageTypes.HostAssetBegin,
				cancellationToken)
			.ConfigureAwait(false))
		{
			return false;
		}

		var offset = 0;
		var index = 0;

		while (offset < content.Length)
		{
			var length = Math.Min(ProtocolLimits.MaxAssetChunkBytes, content.Length - offset);
			var chunk = content.Slice(offset, length).ToArray();

			if (!await StepAsync(pluginId,
					new HostAssetChunkPayload
						{ AssetId = transferId, Index = index, Data = Convert.ToBase64String(chunk) },
					MessageTypes.HostAssetChunk,
					cancellationToken)
				.ConfigureAwait(false))
			{
				return false;
			}

			offset += length;
			index++;
		}

		return await StepAsync(pluginId,
				new HostAssetCommitPayload { AssetId = transferId },
				MessageTypes.HostAssetCommit,
				cancellationToken)
			.ConfigureAwait(false);
	}

	// One unacknowledged step in flight at a time, matching the plugin-to-host uploader exactly: the
	// receiving end never has to reorder anything, and a rejected step stops the transfer where it is.
	private async Task<bool> StepAsync(
		string pluginId,
		object payload,
		string messageType,
		CancellationToken cancellationToken)
	{
		var envelope = new ProtocolEnvelope
		{
			Type = messageType,
			Id = Guid.CreateVersion7().ToString(),
			Payload = JsonSerializer.SerializeToElement(payload, PluginProtocolJson.Options)
		};

		var completion =
			new TaskCompletionSource<HostAssetAckPayload>(TaskCreationOptions.RunContinuationsAsynchronously);
		var key = (pluginId, envelope.Id);
		_pending[key] = completion;

		try
		{
			if (!await _sessions.SendToPlugin(pluginId, envelope, cancellationToken).ConfigureAwait(false))
			{
				return false;
			}

			using var registration = cancellationToken.Register(
				static state => ((TaskCompletionSource<HostAssetAckPayload>)state!).TrySetCanceled(),
				completion);

			var ack = await completion.Task.ConfigureAwait(false);
			return ack.Accepted;
		}
		finally
		{
			_pending.TryRemove(key, out _);
		}
	}
}
