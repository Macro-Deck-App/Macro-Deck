using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.WebSockets;

namespace MacroDeckHost.Plugins;

[SuppressMessage("Design",
	"CA1001:Types that own disposable fields should be disposable",
	Justification = "Callers may still use this connection after its socket ended; Send and Close must then " +
		"act as closed, so the lock is never disposed. It holds no unmanaged resource.")]
public sealed class PluginWebSocketConnection : IPluginConnection
{
	private readonly WebSocket _socket;
	private readonly TimeProvider _timeProvider;
	private readonly SemaphoreSlim _sendLock = new(1, 1);

	public PluginWebSocketConnection(WebSocket socket, string connectionId, TimeProvider timeProvider)
	{
		_socket = socket;
		ConnectionId = connectionId;
		_timeProvider = timeProvider;
	}

	public string ConnectionId { get; }

	public async Task Send(ProtocolEnvelope envelope, CancellationToken cancellationToken = default)
	{
		var bytes = ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope);

		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			if (_socket.State != WebSocketState.Open)
			{
				return;
			}

			// Bounded independently of the caller's own token: a plugin that stops draining its receive
			// buffer parks SendAsync once the OS send buffer fills, which would otherwise wedge every
			// loop that sends on this connection - keepalive, the receive loop's pongs and protocol
			// errors, and capability invokes. Aborting here is what lets the keepalive loop's own
			// silence check ever run again instead of parking on this same call forever.
			await WebSocketMessageIO.SendTextAsync(_socket,
				bytes,
				ProtocolTimeouts.DefaultRequest,
				_timeProvider,
				cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	public async Task Close(int closeCode, string reason, CancellationToken cancellationToken = default)
	{
		await _sendLock.WaitAsync(cancellationToken);
		try
		{
			if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
			{
				await _socket.CloseOutputAsync((WebSocketCloseStatus)closeCode, reason, cancellationToken);
			}
		}
		catch (Exception exception) when (exception is WebSocketException
			or ObjectDisposedException
			or OperationCanceledException)
		{
			// Best effort: a peer that already dropped the transport cannot be told why.
		}
		finally
		{
			_sendLock.Release();
		}
	}
}
