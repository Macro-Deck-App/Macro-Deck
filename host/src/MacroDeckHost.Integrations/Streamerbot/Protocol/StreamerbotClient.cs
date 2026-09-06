using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Streamerbot.Protocol;

internal sealed class StreamerbotClient : IStreamerbotClient
{
	internal const int MaxMessageBytes = 4 * 1024 * 1024;

	private static readonly ILogger _logger =
		IntegrationLog.For<StreamerbotClient>(StreamerbotIntegration.IntegrationId);

	private static readonly TimeSpan _defaultHelloTimeout = TimeSpan.FromSeconds(3);
	private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(10);

	private readonly Func<Uri, CancellationToken, Task<WebSocket>> _connector;
	private readonly TimeSpan _helloTimeout;

	private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending
		= new(StringComparer.Ordinal);

	private readonly TaskCompletionSource<StreamerbotHello?> _hello
		= new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly CancellationTokenSource _cts = new();

	private WebSocket? _socket;
	private Task? _readLoop;
	private int _nextRequestId;
	private int _disconnectRaised;
	private bool _disposed;

	public StreamerbotClient()
		: this(ConnectClientWebSocketAsync)
	{
	}

	internal StreamerbotClient(Func<Uri, CancellationToken, Task<WebSocket>> connector, TimeSpan? helloTimeout = null)
	{
		_connector = connector;
		_helloTimeout = helloTimeout ?? _defaultHelloTimeout;
	}

	public event EventHandler<StreamerbotEventMessage>? EventReceived;

	public event EventHandler<string?>? Disconnected;

	public bool IsConnected => _socket?.State == WebSocketState.Open;

	public async Task<StreamerbotHello?> ConnectAsync(Uri uri, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_socket is not null)
		{
			throw new InvalidOperationException("This Streamer.bot client already ran a session.");
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
		var socket = await _connector(uri, linked.Token).ConfigureAwait(false);
		_socket = socket;
		_readLoop = Task.Run(() => ReadLoopAsync(socket, _cts.Token), CancellationToken.None);

		try
		{
			return await _hello.Task.WaitAsync(_helloTimeout, linked.Token).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			_logger.Debug("No Hello from {Uri} within {Timeout}s; assuming a pre-0.2.5 server",
				uri,
				_helloTimeout.TotalSeconds);
			return null;
		}
	}

	public async Task<JsonElement> RequestAsync(
		string request,
		IReadOnlyDictionary<string, object?>? payload = null,
		CancellationToken cancellationToken = default)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		var socket = _socket;
		if (socket is null || socket.State != WebSocketState.Open)
		{
			throw new StreamerbotRequestException($"Cannot send '{request}': not connected to Streamer.bot.");
		}

		var id = Interlocked.Increment(ref _nextRequestId).ToString(CultureInfo.InvariantCulture);
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

		body["request"] = request;
		body["id"] = id;

		var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[id] = completion;

		try
		{
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
			await SendAsync(socket, body, linked.Token).ConfigureAwait(false);

			var response = await completion.Task.WaitAsync(_requestTimeout, linked.Token).ConfigureAwait(false);
			ThrowIfNotOk(request, response);
			return response;
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
			_logger.Debug(ex, "Error while closing the Streamer.bot socket");
		}

		if (_readLoop is { } loop)
		{
			try
			{
				await loop.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Streamer.bot read loop ended with an error");
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

	private static void ThrowIfNotOk(string request, JsonElement response)
	{
		if (!response.TryGetProperty("status", out var status) ||
			status.ValueKind != JsonValueKind.String ||
			string.Equals(status.GetString(), "ok", StringComparison.Ordinal))
		{
			return;
		}

		var error = response.TryGetProperty("error", out var errorElement) &&
			errorElement.ValueKind == JsonValueKind.String
				? errorElement.GetString()
				: null;

		throw new StreamerbotRequestException(request, error);
	}

	private static StreamerbotHello ParseHello(JsonElement root)
	{
		StreamerbotChallenge? challenge = null;
		if (root.TryGetProperty("authentication", out var authentication) &&
			authentication.ValueKind == JsonValueKind.Object)
		{
			var salt = ReadString(authentication, "salt");
			var value = ReadString(authentication, "challenge");
			if (salt is not null && value is not null)
			{
				challenge = new StreamerbotChallenge(salt, value);
			}
		}

		return new StreamerbotHello(StreamerbotResponses.ReadInstanceInfo(root), challenge);
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private async Task SendAsync(WebSocket socket,
		Dictionary<string, object?> body,
		CancellationToken cancellationToken)
	{
		var bytes = JsonSerializer.SerializeToUtf8Bytes(body, StreamerbotJson.Options);

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
					reason = result.CloseStatusDescription ?? "closed by Streamer.bot";
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
			_logger.Debug(ex, "Streamer.bot read loop failed");
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
			_logger.Debug(ex, "Discarded a malformed Streamer.bot message");
			return;
		}

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object)
			{
				return;
			}

			if (ReadString(root, "id") is { } id && _pending.TryRemove(id, out var pending))
			{
				pending.TrySetResult(root.Clone());
				return;
			}

			if (string.Equals(ReadString(root, "request"), "Hello", StringComparison.Ordinal))
			{
				_hello.TrySetResult(ParseHello(root));
				return;
			}

			if (root.TryGetProperty("event", out var eventElement) &&
				eventElement.ValueKind == JsonValueKind.Object &&
				ReadString(eventElement, "source") is { } source &&
				ReadString(eventElement, "type") is { } type)
			{
				var data = root.TryGetProperty("data", out var dataElement) ? dataElement.Clone() : default;
				EventReceived?.Invoke(this, new StreamerbotEventMessage(source, type, data));
			}
		}
	}

	private void FailPending(string? reason)
	{
		foreach (var (id, completion) in _pending)
		{
			if (_pending.TryRemove(id, out _))
			{
				completion.TrySetException(
					new StreamerbotRequestException($"Streamer.bot connection ended: {reason ?? "unknown"}"));
			}
		}

		_hello.TrySetResult(null);
	}

	private void RaiseDisconnected(string? reason)
	{
		if (Interlocked.Exchange(ref _disconnectRaised, 1) == 0)
		{
			Disconnected?.Invoke(this, reason);
		}
	}
}
