using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Events;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Testing.Fakes;

namespace MacroDeck.Plugin.Testing.Internal;

/// <summary>
/// One accepted, handshake-completed WebSocket connection: the host-side counterpart of
/// <c>PluginSessionConnection</c>. Owns the socket for its lifetime, answers everything a plugin sends
/// unprompted (pings, <c>host.invoke</c>, mid-session <c>capability.declare</c>), and correlates
/// <c>capability.result</c> replies back to whichever <see cref="InvokeAndWaitAsync" /> is waiting for
/// them.
/// </summary>
internal sealed class PluginConnection : IAsyncDisposable
{
	private readonly WebSocket _socket;
	private readonly SessionRecord _session;
	private readonly MacroDeckTestHostOptions _options;
	private readonly ProtocolMessageLog _messages;
	private readonly PluginLogCollector _logs;
	private readonly PluginEventCollector _events;
	private readonly ChannelWriter<ProtocolEnvelope> _fromPlugin;
	private readonly SemaphoreSlim _sending = new(1, 1);

	private readonly ConcurrentDictionary<string, TaskCompletionSource<ProtocolEnvelope>> _pending
		= new(StringComparer.Ordinal);

	/// <summary>Uploads in progress on the plugin-to-host <c>asset.*</c> pipeline, keyed by the uploader's
	/// own asset id. See <see cref="HandleAssetBeginAsync" />'s remarks for why this test host acks them
	/// at all.</summary>
	private readonly ConcurrentDictionary<string, PendingAssetUpload> _pendingAssets = new(StringComparer.Ordinal);

	private readonly FakeIntegrationContext _context = new();
	private readonly CancellationTokenSource _stopping = new();

	public PluginConnection(
		WebSocket socket,
		SessionRecord session,
		bool resumed,
		MacroDeckTestHostOptions options,
		ProtocolMessageLog messages,
		PluginLogCollector logs,
		PluginEventCollector events,
		ChannelWriter<ProtocolEnvelope> fromPlugin)
	{
		_socket = socket;
		_session = session;
		Resumed = resumed;
		_options = options;
		_messages = messages;
		_logs = logs;
		_events = events;
		_fromPlugin = fromPlugin;
	}

	public string SessionId => _session.SessionId;

	public int NegotiatedVersion => _session.NegotiatedVersion;

	public bool Resumed { get; }

	public IReadOnlyList<DeclaredCapability> Declared => _session.Declared;

	public IReadOnlyList<CapabilityNegotiationResult> Accepted => _session.Accepted;

	/// <summary>True once this connection has received <c>session.goodbye</c> from the plugin.</summary>
	public bool PluginSaidGoodbye { get; private set; }

	/// <summary>Runs the receive loop until the socket closes or <paramref name="hostStopping" /> fires.</summary>
	public async Task RunAsync(CancellationToken hostStopping)
	{
		using var linked = CancellationTokenSource.CreateLinkedTokenSource(hostStopping, _stopping.Token);

		try
		{
			await ReceiveLoopAsync(linked.Token).ConfigureAwait(false);
		}
		finally
		{
			foreach (var pending in _pending.Values)
			{
				pending.TrySetException(new IOException("The connection ended before a reply arrived."));
			}

			_pending.Clear();

			foreach (var pendingAsset in _pendingAssets.Values)
			{
				pendingAsset.Buffer.Dispose();
			}

			_pendingAssets.Clear();
		}
	}

	/// <summary>Sends <paramref name="envelope" /> and awaits the <c>capability.result</c> correlated to it.</summary>
	public async Task<ProtocolEnvelope> InvokeAndWaitAsync(ProtocolEnvelope envelope,
		CancellationToken cancellationToken)
	{
		var completion = new TaskCompletionSource<ProtocolEnvelope>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[envelope.Id] = completion;

		await using var registration = cancellationToken.Register(
			static state => ((TaskCompletionSource<ProtocolEnvelope>)state!).TrySetCanceled(),
			completion);

		try
		{
			await SendAsync(envelope, CancellationToken.None).ConfigureAwait(false);
			return await completion.Task.ConfigureAwait(false);
		}
		finally
		{
			_pending.TryRemove(envelope.Id, out _);
		}
	}

	/// <summary>Sends one envelope. Serialized against every other sender on this connection.</summary>
	public async Task SendAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken = default)
	{
		var bytes = ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope);

		await _sending.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await _socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			_sending.Release();
		}

		_messages.Record(ProtocolMessageDirection.ToPlugin, envelope);
	}

	/// <summary>
	/// Sends an envelope this side was obliged to send rather than asked to - a pong, a declare ack, a
	/// host result, a protocol error. These all originate on <see cref="ReceiveLoopAsync" />, so one can
	/// be in flight exactly when <see cref="CloseAsync" /> puts the socket into <c>CloseSent</c>, and the
	/// resulting <see cref="WebSocketException" /> would surface as a spurious failure in whatever test
	/// happened to be closing the connection. An unprompted reply that loses the race to a close has
	/// nothing left to reply to, so it is dropped. <see cref="SendAsync" /> stays strict: a test that
	/// asked for a send must hear about it failing.
	/// </summary>
	private async Task SendUnpromptedAsync(ProtocolEnvelope envelope)
	{
		try
		{
			await SendAsync(envelope, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException)
		{
			// The socket closed under us; there is no peer left to answer.
		}
	}

	/// <summary>Sends raw bytes exactly as given, bypassing envelope construction entirely.</summary>
	public async Task SendRawAsync(string rawJson, CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(rawJson);

		await _sending.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await _socket
				.SendAsync(System.Text.Encoding.UTF8.GetBytes(rawJson),
					WebSocketMessageType.Text,
					endOfMessage: true,
					cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			_sending.Release();
		}
	}

	/// <summary>
	/// Closes the socket from the host side with the given close code. Sends the close frame only,
	/// never waits to read the peer's acknowledgement - <see cref="ReceiveLoopAsync" /> is already
	/// reading this same socket, and a second concurrent read (which the full-duplex
	/// <see cref="WebSocket.CloseAsync" /> performs internally) is not supported by <see cref="WebSocket" />.
	/// The loop observes the close naturally once the peer acks it. Recorded on <see cref="ProtocolMessageLog.Closes" />
	/// regardless of outcome, so a test can prove this side actually sent the code it asked for.
	/// </summary>
	public async Task CloseAsync(int closeCode, string reason, CancellationToken cancellationToken = default)
	{
		await _sending.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await _socket.CloseOutputAsync((WebSocketCloseStatus)closeCode, reason, cancellationToken)
				.ConfigureAwait(false);
		}
		catch (WebSocketException)
		{
			// Already gone; closing a socket that closed itself first is not a failure worth reporting.
		}
		finally
		{
			_sending.Release();
		}

		_messages.RecordClose(closeCode);
	}

	/// <summary>Tells the plugin to hold back everything but pause-exempt traffic.</summary>
	public Task PauseAsync(CancellationToken cancellationToken = default)
		=> SendAsync(new ProtocolEnvelope { Type = MessageTypes.FlowPause, Id = NewId() }, cancellationToken);

	/// <summary>Tells the plugin to resume normal sending.</summary>
	public Task ResumeAsync(CancellationToken cancellationToken = default)
		=> SendAsync(new ProtocolEnvelope { Type = MessageTypes.FlowResume, Id = NewId() }, cancellationToken);

	public async ValueTask DisposeAsync()
	{
		await _stopping.CancelAsync();
		_stopping.Dispose();
		_sending.Dispose();
	}

	private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
	{
		while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
		{
			byte[]? message;

			try
			{
				message = await ReceiveFullMessageAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (WebSocketException)
			{
				return;
			}

			if (message is null)
			{
				return;
			}

			var read = ProtocolEnvelopeReader.Read(message);

			if (!read.Succeeded)
			{
				await SendUnpromptedAsync(ProtocolErrorEnvelope(read.Error!.Code, read.Envelope?.Id))
					.ConfigureAwait(false);
				continue;
			}

			var envelope = read.Envelope!;
			_messages.Record(ProtocolMessageDirection.FromPlugin, envelope);
			await _fromPlugin.WriteAsync(envelope, CancellationToken.None).ConfigureAwait(false);

			await HandleAsync(envelope, cancellationToken).ConfigureAwait(false);
		}
	}

	private async Task HandleAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		switch (envelope.Type)
		{
			case MessageTypes.SessionPing:
				await SendUnpromptedAsync(new ProtocolEnvelope
						{ Type = MessageTypes.SessionPong, Id = NewId(), CorrelationId = envelope.Id })
					.ConfigureAwait(false);
				break;

			case MessageTypes.SessionPong:
			case MessageTypes.ProtocolError:
				// Recorded above; nothing else answers a pong.
				break;

			case MessageTypes.SessionGoodbye:
				// A session the plugin has said goodbye to is not resumable - see MacroDeckTestHost's
				// cleanup after RunAsync returns, which is what actually retires the session record.
				PluginSaidGoodbye = true;
				break;

			case MessageTypes.CapabilityResult:
				if (envelope.CorrelationId is { } correlationId && _pending.TryRemove(correlationId, out var pending))
				{
					pending.TrySetResult(envelope);
				}

				break;

			case MessageTypes.CapabilityDeclare:
				await HandleDeclareAsync(envelope, cancellationToken).ConfigureAwait(false);
				break;

			case MessageTypes.EventPublish:
				RecordEvent(envelope);
				break;

			case MessageTypes.LogPublish:
				RecordLogs(envelope);
				break;

			case MessageTypes.HostInvoke:
				await HandleHostInvokeAsync(envelope, cancellationToken).ConfigureAwait(false);
				break;

			case MessageTypes.AssetBegin:
				await HandleAssetBeginAsync(envelope).ConfigureAwait(false);
				break;

			case MessageTypes.AssetChunk:
				await HandleAssetChunkAsync(envelope).ConfigureAwait(false);
				break;

			case MessageTypes.AssetCommit:
				await HandleAssetCommitAsync(envelope).ConfigureAwait(false);
				break;

			default:
				// state.update and anything else this test host does not model: recorded, not acted on.
				break;
		}
	}

	private async Task HandleDeclareAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		var declared
			= envelope.Payload?.Deserialize<CapabilityDeclarePayload>(PluginProtocolJson.Options)?.Capabilities ?? [];

		var accepted = declared.Select(_options.Negotiate).ToList();
		_session.Declared = declared;
		_session.Accepted = accepted;

		await SendUnpromptedAsync(new ProtocolEnvelope
			{
				Type = MessageTypes.CapabilityDeclareAck,
				Id = NewId(),
				CorrelationId = envelope.Id,
				Payload = JsonSerializer.SerializeToElement(new CapabilityDeclareAckPayload { Capabilities = accepted },
					PluginProtocolJson.Options)
			})
			.ConfigureAwait(false);
	}

	private void RecordEvent(ProtocolEnvelope envelope)
	{
		var payload = envelope.Payload?.Deserialize<EventPublishPayload>(PluginProtocolJson.Options);

		if (payload is null)
		{
			return;
		}

		_events.Record(new PublishedEvent
		{
			EventId = payload.EventId,
			Parameters = payload.Parameters,
			PublishedAt = envelope.SentAt ?? DateTimeOffset.UtcNow
		});
	}

	private void RecordLogs(ProtocolEnvelope envelope)
	{
		var payload = envelope.Payload?.Deserialize<LogPublishPayload>(PluginProtocolJson.Options);

		if (payload is null)
		{
			return;
		}

		foreach (var logEvent in payload.Events)
		{
			_logs.Record(new CollectedLogEvent
			{
				Timestamp = logEvent.Timestamp,
				Level = logEvent.Level,
				SourceContext = logEvent.SourceContext,
				MessageTemplate = logEvent.MessageTemplate,
				Message = logEvent.RenderedMessage,
				Properties = logEvent.Properties ?? new Dictionary<string, string>(StringComparer.Ordinal),
				Exception = logEvent.Exception
			});
		}

		if (payload.Dropped is > 0)
		{
			_logs.RecordDropped(payload.Dropped.Value);
		}
	}

	private async Task HandleHostInvokeAsync(ProtocolEnvelope envelope, CancellationToken cancellationToken)
	{
		var payload = envelope.Payload?.Deserialize<HostInvokePayload>(PluginProtocolJson.Options);

		HostInvokeOutcome outcome;

		if (payload is null)
		{
			outcome = HostInvokeOutcome.Failed(ProtocolErrorCodes.InvalidPayload, "host.invoke requires a payload.");
		}
		else
		{
			outcome = await HostInvokeDispatcher
				.DispatchAsync(_context, _context.Interactions, payload, NegotiatedVersion, cancellationToken)
				.ConfigureAwait(false);
		}

		var reply = outcome.Error is { } error
			? new ProtocolEnvelope
				{ Type = MessageTypes.HostResult, Id = NewId(), CorrelationId = envelope.Id, Error = error }
			: new ProtocolEnvelope
			{
				Type = MessageTypes.HostResult,
				Id = NewId(),
				CorrelationId = envelope.Id,
				Payload = JsonSerializer.SerializeToElement(new HostResultPayload { Data = outcome.Data },
					PluginProtocolJson.Options)
			};

		await SendUnpromptedAsync(reply).ConfigureAwait(false);
	}

	/// <summary>
	/// Starts tracking one plugin-to-host <c>asset.*</c> upload - the pipeline
	/// <c>MusicPlayerCapabilityHandler</c>'s artwork fetch and <c>ActionsCapabilityHandler</c>'s
	/// <c>icon.content</c> both drive through <c>IPluginAssetUploader</c>. This test host has nowhere to
	/// serve the finished bytes back from (there is no <c>/api/ui/resources/{id}</c> here), so acking each
	/// step honestly - verified against what <c>asset.begin</c> declared, exactly as
	/// <see cref="AssetCommitPayload" />'s own remarks say a receiver does - is the whole job: it is what
	/// lets <c>IPluginAssetUploader.UploadAsync</c> complete against this host exactly as it would against
	/// a real one, so a capability handler that uploads bytes can be conformance-tested at all.
	/// </summary>
	private async Task HandleAssetBeginAsync(ProtocolEnvelope envelope)
	{
		var payload = envelope.Payload?.Deserialize<AssetBeginPayload>(PluginProtocolJson.Options);

		if (payload is null)
		{
			return;
		}

		_pendingAssets[payload.AssetId] = new PendingAssetUpload(payload.TotalBytes, payload.ContentHash);

		await AcknowledgeAssetAsync(envelope, payload.AssetId, index: null, accepted: true).ConfigureAwait(false);
	}

	private async Task HandleAssetChunkAsync(ProtocolEnvelope envelope)
	{
		var payload = envelope.Payload?.Deserialize<AssetChunkPayload>(PluginProtocolJson.Options);

		if (payload is null)
		{
			return;
		}

		if (_pendingAssets.TryGetValue(payload.AssetId, out var pending))
		{
			var bytes = Convert.FromBase64String(payload.Data);
			pending.Buffer.Write(bytes, 0, bytes.Length);
		}

		await AcknowledgeAssetAsync(envelope, payload.AssetId, payload.Index, accepted: true).ConfigureAwait(false);
	}

	/// <summary>
	/// Finishes an upload: accepted only when the reassembled bytes match both the total length and the
	/// content hash <c>asset.begin</c> declared up front, exactly as a real receiver verifies before
	/// treating the asset as complete. An unknown asset id - <c>asset.begin</c> was missed or already
	/// committed - is rejected the same way a real receiver would refuse to finalise something it never
	/// started.
	/// </summary>
	private async Task HandleAssetCommitAsync(ProtocolEnvelope envelope)
	{
		var payload = envelope.Payload?.Deserialize<AssetCommitPayload>(PluginProtocolJson.Options);

		if (payload is null)
		{
			return;
		}

		var accepted = false;

		if (_pendingAssets.TryRemove(payload.AssetId, out var pending))
		{
			var bytes = pending.Buffer.ToArray();
			accepted = bytes.Length == pending.TotalBytes &&
				string.Equals(AssetContentHash.Compute(bytes), pending.ContentHash, StringComparison.Ordinal);
			pending.Buffer.Dispose();
		}

		await AcknowledgeAssetAsync(envelope, payload.AssetId, index: null, accepted).ConfigureAwait(false);
	}

	private Task AcknowledgeAssetAsync(ProtocolEnvelope envelope, string assetId, int? index, bool accepted)
		=> SendUnpromptedAsync(new ProtocolEnvelope
		{
			Type = MessageTypes.AssetAck,
			Id = NewId(),
			CorrelationId = envelope.Id,
			Payload = JsonSerializer.SerializeToElement(
				new AssetAckPayload { AssetId = assetId, Index = index, Accepted = accepted },
				PluginProtocolJson.Options)
		});

	/// <summary>Bytes received so far for one in-progress upload, plus what <c>asset.begin</c> declared
	/// they must add up to.</summary>
	private sealed class PendingAssetUpload(int totalBytes, string contentHash)
	{
		public int TotalBytes { get; } = totalBytes;

		public string ContentHash { get; } = contentHash;

		public MemoryStream Buffer { get; } = new();
	}

	private Task<byte[]?> ReceiveFullMessageAsync(CancellationToken cancellationToken)
		=> WebSocketIo.ReceiveOneMessageAsync(_socket, _options.Limits.MaxMessageBytes, cancellationToken);

	private static ProtocolEnvelope ProtocolErrorEnvelope(string code, string? correlationId)
		=> new()
		{
			Type = MessageTypes.ProtocolError,
			Id = NewId(),
			CorrelationId = correlationId,
			Error = new ProtocolError { Code = code, Message = ProtocolErrorMessages.For(code), Retryable = false }
		};

	private static string NewId() => Guid.CreateVersion7().ToString();
}
