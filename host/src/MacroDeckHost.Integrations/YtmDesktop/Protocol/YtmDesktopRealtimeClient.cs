using System.Globalization;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.YtmDesktop.Protocol;

internal sealed class YtmDesktopRealtimeClient : IYtmDesktopRealtimeClient
{
	internal const int MaxMessageBytes = 8 * 1024 * 1024;

	private static readonly ILogger _logger =
		IntegrationLog.For<YtmDesktopRealtimeClient>(YtmDesktopIntegration.IntegrationId);

	private static readonly TimeSpan _defaultHandshakeTimeout = TimeSpan.FromSeconds(10);

	private readonly Func<Uri, CancellationToken, Task<WebSocket>> _connector;
	private readonly TimeSpan _handshakeTimeout;

	private readonly TaskCompletionSource<bool> _open = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly TaskCompletionSource<bool> _namespaceConnected
		= new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly CancellationTokenSource _cts = new();

	private WebSocket? _socket;
	private Task? _readLoop;
	private Timer? _watchdog;
	private TimeSpan _pingInterval = TimeSpan.FromSeconds(25);
	private TimeSpan _pingTimeout = TimeSpan.FromSeconds(20);
	private int _disconnectRaised;
	private bool _disposed;

	public YtmDesktopRealtimeClient()
		: this(ConnectClientWebSocketAsync)
	{
	}

	internal YtmDesktopRealtimeClient(
		Func<Uri, CancellationToken, Task<WebSocket>> connector,
		TimeSpan? handshakeTimeout = null)
	{
		_connector = connector;
		_handshakeTimeout = handshakeTimeout ?? _defaultHandshakeTimeout;
	}

	public event EventHandler<JsonElement>? StateUpdated;

	public event EventHandler<YtmPlaylist>? PlaylistCreated;

	public event EventHandler<string>? PlaylistDeleted;

	public event EventHandler<string?>? Disconnected;

	public bool IsConnected => _socket?.State == WebSocketState.Open;

	public async Task ConnectAsync(Uri uri, string token, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_socket is not null)
		{
			throw new InvalidOperationException("This ytmdesktop realtime client already ran a session.");
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
		var socket = await _connector(uri, linked.Token).ConfigureAwait(false);
		_socket = socket;
		_readLoop = Task.Run(() => ReadLoopAsync(socket, _cts.Token), CancellationToken.None);

		try
		{
			await _open.Task.WaitAsync(_handshakeTimeout, linked.Token).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			throw new YtmDesktopApiException(CreateTimeoutMessage(uri, "the Engine.IO handshake"),
				HttpStatusCode.GatewayTimeout);
		}

		await SendRawAsync(SocketIoFrames.Connect(token), linked.Token).ConfigureAwait(false);

		try
		{
			await _namespaceConnected.Task.WaitAsync(_handshakeTimeout, linked.Token).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			throw new YtmDesktopApiException(CreateTimeoutMessage(uri, "the realtime namespace connect"),
				HttpStatusCode.GatewayTimeout);
		}
	}

	public async Task DisconnectAsync()
	{
		var socket = _socket;
		if (socket is null)
		{
			return;
		}

		try
		{
			await SendRawAsync(SocketIoFrames.Disconnect(), CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Could not send the disconnect frame to the ytmdesktop realtime socket");
		}

		await _cts.CancelAsync().ConfigureAwait(false);

		try
		{
			if (socket.State == WebSocketState.Open)
			{
				using var closeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
				await socket
					.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closeTimeout.Token)
					.ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Error while closing the ytmdesktop realtime socket");
		}

		if (_readLoop is { } loop)
		{
			try
			{
				await loop.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "ytmdesktop realtime read loop ended with an error");
			}
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}

		_disposed = true;
		_cts.Cancel();
		_socket?.Dispose();
		_socket = null;
		_sendLock.Dispose();
		_watchdog?.Dispose();
		_cts.Dispose();
		RaiseDisconnected("disposed");
	}

	private static async Task<WebSocket> ConnectClientWebSocketAsync(Uri uri, CancellationToken cancellationToken)
	{
		var socket = new ClientWebSocket();
		try
		{
			await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
			return socket;
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	private string CreateTimeoutMessage(Uri uri, string step)
		=> string.Create(CultureInfo.InvariantCulture,
			$"The Companion Server at {uri} did not complete {step} within {_handshakeTimeout.TotalSeconds} seconds.");

	private async Task ReadLoopAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[8192];
		using var message = new MemoryStream();
		string? reason = null;

		try
		{
			while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
			{
				var result = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);
				if (result.MessageType == WebSocketMessageType.Close)
				{
					reason = result.CloseStatusDescription ?? "closed by the Companion Server";
					break;
				}

				if (result.MessageType == WebSocketMessageType.Binary)
				{
					if (result.EndOfMessage)
					{
						message.SetLength(0);
						ResetWatchdog();
					}

					continue;
				}

				message.Write(buffer, 0, result.Count);
				if (message.Length > MaxMessageBytes)
				{
					reason = "message exceeded the size limit";
					break;
				}

				if (!result.EndOfMessage)
				{
					continue;
				}

				var text = Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
				message.SetLength(0);

				reason = await DispatchAsync(text, cancellationToken).ConfigureAwait(false);
				if (reason is not null)
				{
					break;
				}
			}
		}
		catch (OperationCanceledException)
		{
			reason = "disconnected";
		}
		catch (Exception ex)
		{
			reason = ex.Message;
			_logger.Debug(ex, "The ytmdesktop realtime read loop failed");
		}

		EndSession(reason);
	}

	private async Task<string?> DispatchAsync(string text, CancellationToken cancellationToken)
	{
		if (!SocketIoFrames.TryParse(text, out var frame))
		{
			_logger.Debug("Discarded a malformed ytmdesktop realtime frame");
			ResetWatchdog();
			return null;
		}

		string? reason;
		switch (frame.Engine)
		{
			case EngineIoType.Open:
				HandleOpen(frame.Payload);
				reason = null;
				break;

			case EngineIoType.Ping:
				await SendRawAsync(SocketIoFrames.Pong(), cancellationToken).ConfigureAwait(false);
				reason = null;
				break;

			case EngineIoType.Close:
				reason = "closed by the Companion Server";
				break;

			case EngineIoType.Message:
				reason = HandleMessage(frame);
				break;

			default:
				reason = null;
				break;
		}

		ResetWatchdog();
		return reason;
	}

	private void HandleOpen(string payload)
	{
		if (!SocketIoFrames.TryReadOpen(payload, out _, out var pingInterval, out var pingTimeout))
		{
			_logger.Debug("Discarded a malformed Engine.IO OPEN frame");
			return;
		}

		_pingInterval = pingInterval;
		_pingTimeout = pingTimeout;
		_open.TrySetResult(true);
	}

	private string? HandleMessage(SocketIoFrame frame)
	{
		switch (frame.Socket)
		{
			case SocketIoType.Connect:
				if (SocketIoFrames.IsRealtimeNamespace(frame.Namespace))
				{
					_namespaceConnected.TrySetResult(true);
				}

				return null;

			case SocketIoType.ConnectError:
				HandleConnectError(frame.Payload);
				return "the realtime namespace refused the connection";

			case SocketIoType.Disconnect:
				return SocketIoFrames.IsRealtimeNamespace(frame.Namespace)
					? "disconnected by the Companion Server"
					: null;

			case SocketIoType.Event:
				HandleEvent(frame);
				return null;

			default:
				return null;
		}
	}

	private void HandleConnectError(string payload)
	{
		var message = $"The Companion Server refused the realtime connection: {payload}";
		Exception exception = MentionsUnauthenticated(payload)
			? new YtmDesktopAuthorizationException(message)
			: new YtmDesktopApiException(message, HttpStatusCode.BadGateway);

		_namespaceConnected.TrySetException(exception);
	}

	private void HandleEvent(SocketIoFrame frame)
	{
		if (!SocketIoFrames.IsRealtimeNamespace(frame.Namespace))
		{
			return;
		}

		if (!SocketIoFrames.TryReadEvent(frame.Payload, out var name, out var argument))
		{
			_logger.Debug("Discarded a malformed ytmdesktop realtime event frame");
			return;
		}

		switch (name)
		{
			case "state-update":
				StateUpdated?.Invoke(this, argument);
				break;

			case "playlist-created":
				if (TryReadPlaylist(argument, out var playlist))
				{
					PlaylistCreated?.Invoke(this, playlist);
				}

				break;

			case "playlist-deleted":
				if (argument.ValueKind == JsonValueKind.String && argument.GetString() is { } id)
				{
					PlaylistDeleted?.Invoke(this, id);
				}

				break;
		}
	}

	private static bool TryReadPlaylist(JsonElement argument, out YtmPlaylist playlist)
	{
		playlist = null!;
		if (argument.ValueKind != JsonValueKind.Object)
		{
			return false;
		}

		var id = argument.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
			? idElement.GetString()
			: null;

		var title = argument.TryGetProperty("title", out var titleElement) &&
			titleElement.ValueKind == JsonValueKind.String
				? titleElement.GetString()
				: null;

		if (id is null || title is null)
		{
			return false;
		}

		playlist = new YtmPlaylist(id, title);
		return true;
	}

	private static bool MentionsUnauthenticated(string payload)
	{
		if (payload.Contains(YtmErrorCodes.Unauthenticated, StringComparison.Ordinal))
		{
			return true;
		}

		try
		{
			using var document = JsonDocument.Parse(payload);
			var root = document.RootElement;
			return HasUnauthenticatedProperty(root, "code") || HasUnauthenticatedProperty(root, "message");
		}
		catch (JsonException)
		{
			return false;
		}
	}

	private static bool HasUnauthenticatedProperty(JsonElement root, string property)
		=> root.ValueKind == JsonValueKind.Object &&
			root.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.String &&
			(value.GetString()?.Contains(YtmErrorCodes.Unauthenticated, StringComparison.Ordinal) ?? false);

	private async Task SendRawAsync(string text, CancellationToken cancellationToken)
	{
		var socket = _socket;
		if (socket is null || socket.State != WebSocketState.Open)
		{
			return;
		}

		var bytes = Encoding.UTF8.GetBytes(text);

		await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await socket.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			_sendLock.Release();
		}
	}

	private void ResetWatchdog()
	{
		var due = _pingInterval + _pingTimeout;
		if (due <= TimeSpan.Zero)
		{
			return;
		}

		if (_watchdog is null)
		{
			_watchdog = new Timer(OnWatchdogElapsed, null, due, Timeout.InfiniteTimeSpan);
		}
		else
		{
			_watchdog.Change(due, Timeout.InfiniteTimeSpan);
		}
	}

	private void OnWatchdogElapsed(object? state)
	{
		EndSession("ping timeout");
		_cts.Cancel();
	}

	private void EndSession(string? reason)
	{
		_watchdog?.Dispose();
		RaiseDisconnected(reason);
	}

	private void RaiseDisconnected(string? reason)
	{
		if (Interlocked.Exchange(ref _disconnectRaised, 1) == 0)
		{
			Disconnected?.Invoke(this, reason);
		}
	}
}
