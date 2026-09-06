using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Assets;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;

namespace MacroDeckHost.Tests.PluginContractTests.Harness;

internal sealed class InMemoryPluginLink : IPluginSocket, IPluginConnection
{
	private readonly Channel<byte[]> _toPlugin = Channel.CreateUnbounded<byte[]>();

	private readonly Lock _gate = new();
	private readonly List<ProtocolEnvelope> _sentByHost = [];
	private readonly List<HostCallbackResult> _hostResults = [];

	private readonly string _sessionId;

	public IPluginAssetReceiver? AssetReceiver { get; set; }

	public Func<string, CancellationToken, Task>? StateUpdateHandler { get; set; }

	/// <summary>Routes the plugin's <c>host.asset.ack</c> back to the host's asset sender, which is what
	/// lets a chunked host-to-plugin transfer advance past its first step.</summary>
	public Func<ProtocolEnvelope, bool>? HostAssetAckHandler { get; set; }

	/// <summary>Routes the plugin's <c>host.invoke</c> calls into the host's callback router.</summary>
	public Func<string, HostInvokePayload, CancellationToken, Task<HostCallbackResult>>? HostInvokeHandler { get; set; }

	public IReadOnlyList<HostCallbackResult> HostResults
	{
		get
		{
			lock (_gate)
			{
				return [.. _hostResults];
			}
		}
	}

	/// <summary>Puts bytes on the wire exactly as a plugin process would, with no serializer of the
	/// host's own in between. Byte fidelity cannot be shown any other way: anything that re-serializes
	/// the payload on the way in normalises it before the host ever sees it.</summary>
	public Task SendRawFromPluginAsync(byte[] utf8) => SendAsync(utf8, CancellationToken.None);

	public Func<LogPublishPayload, CancellationToken, Task>? LogPublishHandler { get; set; }

	public string AssetPluginId { get; set; } = string.Empty;

	private bool _dropNextReply;
	private TaskCompletionSource? _heldReply;

	public InMemoryPluginLink(string sessionId) => _sessionId = sessionId;

	public event Action<ProtocolEnvelope>? CapabilityResultReceived;

	public IReadOnlyList<ProtocolEnvelope> SentByHost
	{
		get
		{
			lock (_gate)
			{
				return [.. _sentByHost];
			}
		}
	}

	public int? CloseCode { get; private set; }

	public string? CloseDescription { get; private set; }

	public void DropNextReply() => _dropNextReply = true;

	public IDisposable HoldNextReply()
	{
		var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_heldReply = held;
		return new Releaser(held);
	}


	public string ConnectionId => "in-memory-link";

	public Task Send(ProtocolEnvelope envelope, CancellationToken cancellationToken = default)
	{
		lock (_gate)
		{
			_sentByHost.Add(envelope);
		}

		_toPlugin.Writer.TryWrite(ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope));
		return Task.CompletedTask;
	}

	Task IPluginConnection.Close(int closeCode, string reason, CancellationToken cancellationToken)
	{
		CloseCode = closeCode;
		CloseDescription = reason;
		_toPlugin.Writer.TryComplete();
		return Task.CompletedTask;
	}


	public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken)
	{
		try
		{
			return await _toPlugin.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
		}
		catch (ChannelClosedException)
		{
			return null;
		}
	}

	public async Task SendAsync(ReadOnlyMemory<byte> utf8, CancellationToken cancellationToken)
	{
		var read = ProtocolEnvelopeReader.Read(utf8.Span);
		if (read.Envelope is not { } envelope)
		{
			return;
		}

		if (string.Equals(envelope.Type, MessageTypes.SessionHello, StringComparison.Ordinal))
		{
			RespondWelcome(envelope);
			return;
		}

		if (string.Equals(envelope.Type, MessageTypes.SessionPing, StringComparison.Ordinal))
		{
			_toPlugin.Writer.TryWrite(ProtocolEnvelopeWriter.WriteToUtf8Bytes(new ProtocolEnvelope
			{
				Type = MessageTypes.SessionPong, Id = Guid.CreateVersion7().ToString(), CorrelationId = envelope.Id
			}));
			return;
		}

		if (AssetReceiver is not null && TryHandleAssetMessage(envelope))
		{
			return;
		}

		if (string.Equals(envelope.Type, MessageTypes.HostAssetAck, StringComparison.Ordinal))
		{
			HostAssetAckHandler?.Invoke(envelope);
			return;
		}

		if (string.Equals(envelope.Type, MessageTypes.HostInvoke, StringComparison.Ordinal))
		{
			await HandleHostInvokeAsync(envelope).ConfigureAwait(false);
			return;
		}

		if (string.Equals(envelope.Type, MessageTypes.StateUpdate, StringComparison.Ordinal))
		{
			if (StateUpdateHandler is { } handler &&
				envelope.Payload?.Deserialize<StateUpdatePayload>(PluginProtocolJson.Options) is
					{ Kind.Length: > 0 } payload)
			{
				_ = handler(payload.Kind, CancellationToken.None);
			}

			return;
		}

		if (string.Equals(envelope.Type, MessageTypes.LogPublish, StringComparison.Ordinal))
		{
			if (LogPublishHandler is { } logHandler &&
				envelope.Payload?.Deserialize<LogPublishPayload>(PluginProtocolJson.Options) is { } payload)
			{
				_ = logHandler(payload, CancellationToken.None);
			}

			return;
		}

		if (!string.Equals(envelope.Type, MessageTypes.CapabilityResult, StringComparison.Ordinal))
		{
			return;
		}

		if (_dropNextReply)
		{
			_dropNextReply = false;
			return;
		}

		if (Interlocked.Exchange(ref _heldReply, null) is { } held)
		{
			await held.Task.ConfigureAwait(false);
		}

		CapabilityResultReceived?.Invoke(envelope);
	}

	public Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken cancellationToken)
	{
		CloseCode ??= (int)status;
		CloseDescription = description;
		_toPlugin.Writer.TryComplete();
		return Task.CompletedTask;
	}

	public Task AbortAsync()
	{
		_toPlugin.Writer.TryComplete();
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		_toPlugin.Writer.TryComplete();
		return ValueTask.CompletedTask;
	}

	/// <summary>
	/// Routes a plugin's <c>host.invoke</c> exactly the way <c>PluginWebSocketEndpoint</c> does, reading
	/// the payload out of the received envelope rather than out of anything the test handed over. A
	/// harness that read the payload some other way would hide whatever the real receive path does to a
	/// provider's bytes.
	/// </summary>
	private async Task HandleHostInvokeAsync(ProtocolEnvelope envelope)
	{
		if (HostInvokeHandler is not { } handler)
		{
			return;
		}

		HostInvokePayload? payload;
		try
		{
			payload = envelope.Payload?.Deserialize<HostInvokePayload>(PluginProtocolJson.Options);
		}
		catch (JsonException)
		{
			payload = null;
		}

		if (payload is null)
		{
			return;
		}

		var result = await handler(envelope.Id, payload, CancellationToken.None).ConfigureAwait(false);

		lock (_gate)
		{
			_hostResults.Add(result);
		}

		_toPlugin.Writer.TryWrite(ProtocolEnvelopeWriter.WriteToUtf8Bytes(new ProtocolEnvelope
		{
			Type = MessageTypes.HostResult,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = envelope.Id,
			Error = result.Error,
			Payload = result.Error is null
				? JsonSerializer.SerializeToElement(new HostResultPayload { Data = result.Data },
					PluginProtocolJson.Options)
				: null
		}));
	}

	private void RespondWelcome(ProtocolEnvelope hello)
	{
		var payload = hello.Payload?.Deserialize<SessionHelloPayload>(PluginProtocolJson.Options);

		_toPlugin.Writer.TryWrite(ProtocolEnvelopeWriter.WriteToUtf8Bytes(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionWelcome,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = hello.Id,
			Payload = JsonSerializer.SerializeToElement(new SessionWelcomePayload
				{
					SessionId = payload?.SessionId ?? _sessionId,
					Resumed = !string.IsNullOrEmpty(payload?.ResumeSessionId)
				},
				PluginProtocolJson.Options)
		}));
	}

	private bool TryHandleAssetMessage(ProtocolEnvelope envelope)
	{
		string assetId;
		int? index = null;
		AssetOperationResult result;

		switch (envelope.Type)
		{
			case MessageTypes.AssetBegin:
				var begin = envelope.Payload!.Value.Deserialize<AssetBeginPayload>(PluginProtocolJson.Options)!;
				assetId = begin.AssetId;
				result = AssetReceiver!.Begin(AssetPluginId,
					begin.AssetId,
					begin.Kind,
					begin.MimeType,
					begin.TotalBytes,
					begin.ContentHash);
				break;

			case MessageTypes.AssetChunk:
				var chunk = envelope.Payload!.Value.Deserialize<AssetChunkPayload>(PluginProtocolJson.Options)!;
				assetId = chunk.AssetId;
				index = chunk.Index;
				result = AssetReceiver!.Chunk(AssetPluginId,
					chunk.AssetId,
					chunk.Index,
					Convert.FromBase64String(chunk.Data));
				break;

			case MessageTypes.AssetCommit:
				var commit = envelope.Payload!.Value.Deserialize<AssetCommitPayload>(PluginProtocolJson.Options)!;
				assetId = commit.AssetId;
				result = AssetReceiver!.Commit(AssetPluginId, commit.AssetId);
				break;

			default:
				return false;
		}

		_toPlugin.Writer.TryWrite(ProtocolEnvelopeWriter.WriteToUtf8Bytes(new ProtocolEnvelope
		{
			Type = MessageTypes.AssetAck,
			Id = Guid.CreateVersion7().ToString(),
			CorrelationId = envelope.Id,
			Payload = JsonSerializer.SerializeToElement(
				new AssetAckPayload { AssetId = assetId, Index = index, Accepted = result.Accepted },
				PluginProtocolJson.Options)
		}));

		return true;
	}

	private sealed class Releaser(TaskCompletionSource held) : IDisposable
	{
		public void Dispose() => held.TrySetResult();
	}
}
