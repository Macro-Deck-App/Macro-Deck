using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Authentication;
using System.Text.Json;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.HomeAssistant.Protocol;

internal sealed class HomeAssistantClient : IHomeAssistantClient
{
	internal const int MaxMessageBytes = 16 * 1024 * 1024;

	private static readonly TimeSpan _defaultHandshakeTimeout = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan _commandTimeout = TimeSpan.FromSeconds(10);

	private static readonly ILogger _logger =
		IntegrationLog.For<HomeAssistantClient>(HomeAssistantIntegration.IntegrationId);

	private readonly Func<Uri, CancellationToken, Task<WebSocket>> _connector;
	private readonly TimeSpan _handshakeTimeout;

	private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();

	private readonly TaskCompletionSource _authRequired = new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly TaskCompletionSource<HomeAssistantAuthOutcome> _authenticated
		= new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly CancellationTokenSource _cts = new();

	private WebSocket? _socket;
	private Task? _readLoop;
	private int _nextCommandId;
	private int _disconnectRaised;
	private int _disposed;

	public HomeAssistantClient()
		: this(ConnectClientWebSocketAsync)
	{
	}

	internal HomeAssistantClient(
		Func<Uri, CancellationToken, Task<WebSocket>> connector,
		TimeSpan? handshakeTimeout = null)
	{
		_connector = connector;
		_handshakeTimeout = handshakeTimeout ?? _defaultHandshakeTimeout;
	}

	public event EventHandler<HomeAssistantEventMessage>? EventReceived;

	public event EventHandler<string?>? Disconnected;

	public bool IsConnected => _socket?.State == WebSocketState.Open;

	public async Task<HomeAssistantHello> ConnectAsync(Uri uri, string token, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed == 1, this);
		if (_socket is not null)
		{
			throw new InvalidOperationException("This Home Assistant client already ran a session.");
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

		WebSocket socket;
		try
		{
			socket = await _connector(uri, linked.Token).ConfigureAwait(false);
		}
		catch (Exception ex) when (IsTlsFailure(ex))
		{
			throw new HomeAssistantTlsException($"The certificate presented by {uri.Host} could not be validated.",
				ex);
		}

		_socket = socket;
		_readLoop = Task.Run(() => ReadLoopAsync(socket, _cts.Token), CancellationToken.None);

		try
		{
			// A server that answers auth_ok without greeting first is tolerated; anything else that
			// keeps the socket open without speaking this protocol runs into the timeout.
			var greeting = await Task.WhenAny(_authRequired.Task, _authenticated.Task)
				.WaitAsync(_handshakeTimeout, linked.Token)
				.ConfigureAwait(false);

			if (greeting != _authenticated.Task)
			{
				await SendAsync(socket,
						new Dictionary<string, object?>(StringComparer.Ordinal)
						{
							["type"] = "auth",
							["access_token"] = token
						},
						linked.Token)
					.ConfigureAwait(false);
			}

			var outcome = await _authenticated.Task.WaitAsync(_handshakeTimeout, linked.Token).ConfigureAwait(false);

			return outcome.Status switch
			{
				HomeAssistantAuthStatus.Ok => new HomeAssistantHello(outcome.Version),
				HomeAssistantAuthStatus.Invalid => throw new HomeAssistantAuthenticationException(outcome.Message ??
					"Home Assistant rejected the access token."),
				_ => throw new HomeAssistantRequestException("The connection closed during the handshake.")
			};
		}
		catch (TimeoutException ex)
		{
			throw new HomeAssistantRequestException(
				$"No Home Assistant handshake from {uri} within {_handshakeTimeout.TotalSeconds} seconds.",
				ex);
		}
	}

	public async Task<JsonElement> SendCommandAsync(
		string type,
		IReadOnlyDictionary<string, object?>? payload = null,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed == 1, this);
		var socket = _socket;
		if (socket is null || socket.State != WebSocketState.Open)
		{
			throw new HomeAssistantRequestException($"Cannot send '{type}': not connected to Home Assistant.");
		}

		var id = Interlocked.Increment(ref _nextCommandId);
		var body = new Dictionary<string, object?>(StringComparer.Ordinal);
		if (payload is not null)
		{
			foreach (var (key, value) in payload)
			{
				if (value is not null)
				{
					body[key] = value;
				}
			}
		}

		body["id"] = id;
		body["type"] = type;

		var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[id] = completion;

		try
		{
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
			await SendAsync(socket, body, linked.Token).ConfigureAwait(false);

			return await completion.Task.WaitAsync(_commandTimeout, linked.Token).ConfigureAwait(false);
		}
		finally
		{
			_pending.TryRemove(id, out _);
		}
	}

	public async Task DisconnectAsync()
	{
		var socket = _socket;
		if (socket is null)
		{
			return;
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
			_logger.Debug(ex, "Error while closing the Home Assistant socket");
		}

		if (_readLoop is { } loop)
		{
			try
			{
				await loop.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Home Assistant read loop ended with an error");
			}
		}
	}

	public void Dispose()
	{
		if (Interlocked.Exchange(ref _disposed, 1) == 1)
		{
			return;
		}

		_cts.Cancel();
		_socket?.Dispose();
		_socket = null;
		_sendLock.Dispose();
		_cts.Dispose();
		FailPending("disposed");
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

	private static bool IsTlsFailure(Exception exception)
	{
		for (var current = exception; current is not null; current = current.InnerException)
		{
			if (current is AuthenticationException)
			{
				return true;
			}
		}

		return false;
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.ValueKind == JsonValueKind.Object &&
			element.TryGetProperty(property, out var value) &&
			value.ValueKind == JsonValueKind.String
				? value.GetString()
				: null;

	private async Task SendAsync(
		WebSocket socket,
		Dictionary<string, object?> body,
		CancellationToken cancellationToken)
	{
		var bytes = JsonSerializer.SerializeToUtf8Bytes(body, HomeAssistantJson.Options);

		await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			await socket
				.SendAsync(bytes, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
				.ConfigureAwait(false);
		}
		finally
		{
			_sendLock.Release();
		}
	}

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
					reason = result.CloseStatusDescription ?? "closed by Home Assistant";
					break;
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

				Dispatch(new ReadOnlyMemory<byte>(message.GetBuffer(), 0, (int)message.Length));
				message.SetLength(0);
			}
		}
		catch (OperationCanceledException)
		{
			reason = "disconnected";
		}
		catch (Exception ex)
		{
			reason = ex.Message;
			_logger.Debug(ex, "Home Assistant read loop failed");
		}

		FailPending(reason);
		RaiseDisconnected(reason);
	}

	private void Dispatch(ReadOnlyMemory<byte> payload)
	{
		JsonDocument document;
		try
		{
			document = JsonDocument.Parse(payload);
		}
		catch (JsonException ex)
		{
			_logger.Debug(ex, "Discarded a malformed Home Assistant message");
			return;
		}

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return;
			}

			switch (ReadString(root, "type"))
			{
				case "auth_required":
					_authRequired.TrySetResult();
					break;
				case "auth_ok":
					_authenticated.TrySetResult(new HomeAssistantAuthOutcome(HomeAssistantAuthStatus.Ok,
						ReadString(root, "ha_version"),
						null));
					break;
				case "auth_invalid":
					_authenticated.TrySetResult(new HomeAssistantAuthOutcome(HomeAssistantAuthStatus.Invalid,
						null,
						ReadString(root, "message")));
					break;
				case "event":
					DispatchEvent(root);
					break;
				case "result":
					DispatchResult(root);
					break;
				case "pong":
					if (TryTakePending(root, out var pong))
					{
						pong.TrySetResult(default);
					}

					break;
				default:
					_logger.Debug("Ignored a Home Assistant message of an unknown type");
					break;
			}
		}
	}

	private void DispatchEvent(JsonElement root)
	{
		if (!root.TryGetProperty("event", out var element) ||
			element.ValueKind != JsonValueKind.Object ||
			ReadString(element, "event_type") is not { Length: > 0 } eventType)
		{
			return;
		}

		if (string.Equals(eventType, "state_reported", StringComparison.Ordinal))
		{
			return;
		}

		var data = element.TryGetProperty("data", out var dataElement) ? dataElement.Clone() : default;
		EventReceived?.Invoke(this, new HomeAssistantEventMessage(eventType, data));
	}

	private void DispatchResult(JsonElement root)
	{
		if (!TryTakePending(root, out var pending))
		{
			return;
		}

		if (root.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
		{
			var error = root.TryGetProperty("error", out var errorElement) ? errorElement : default;
			pending.TrySetException(new HomeAssistantRequestException(ReadString(error, "code") ?? "unknown_error",
				ReadString(error, "message")));
			return;
		}

		pending.TrySetResult(root.TryGetProperty("result", out var result) ? result.Clone() : default);
	}

	private bool TryTakePending(JsonElement root, out TaskCompletionSource<JsonElement> pending)
	{
		if (root.TryGetProperty("id", out var id) &&
			id.ValueKind == JsonValueKind.Number &&
			id.TryGetInt32(out var value))
		{
			return _pending.TryRemove(value, out pending!);
		}

		pending = null!;
		return false;
	}

	private void FailPending(string? reason)
	{
		foreach (var (id, completion) in _pending)
		{
			if (_pending.TryRemove(id, out _))
			{
				completion.TrySetException(
					new HomeAssistantRequestException($"Home Assistant connection ended: {reason ?? "unknown"}"));
			}
		}

		_authRequired.TrySetResult();
		_authenticated.TrySetResult(new HomeAssistantAuthOutcome(HomeAssistantAuthStatus.Closed, null, reason));
	}

	private void RaiseDisconnected(string? reason)
	{
		if (Interlocked.Exchange(ref _disconnectRaised, 1) == 0)
		{
			Disconnected?.Invoke(this, reason);
		}
	}
}
