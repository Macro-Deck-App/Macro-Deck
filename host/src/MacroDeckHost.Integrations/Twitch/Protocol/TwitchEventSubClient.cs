using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal sealed class TwitchEventSubClient : ITwitchEventSubClient
{
	internal const int MaxMessageBytes = 1024 * 1024;

	private static readonly ILogger _logger =
		IntegrationLog.For<TwitchEventSubClient>(TwitchIntegration.IntegrationId);

	private static readonly TimeSpan _welcomeTimeout = TimeSpan.FromSeconds(15);

	private readonly Func<Uri, CancellationToken, Task<WebSocket>> _connector;
	private readonly TimeSpan _timeout;

	private readonly TaskCompletionSource<TwitchSessionWelcome?> _welcome
		= new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly CancellationTokenSource _cts = new();

	private WebSocket? _socket;
	private Task? _readLoop;
	private volatile string? _endReason;
	private int _disconnectRaised;
	private bool _disposed;

	public TwitchEventSubClient()
		: this(ConnectClientWebSocketAsync)
	{
	}

	internal TwitchEventSubClient(
		Func<Uri, CancellationToken, Task<WebSocket>> connector,
		TimeSpan? welcomeTimeout = null)
	{
		_connector = connector;
		_timeout = welcomeTimeout ?? _welcomeTimeout;
	}

	public event EventHandler<TwitchEventSubMessage>? MessageReceived;

	public event EventHandler<string?>? Disconnected;

	public bool IsConnected => _socket?.State == WebSocketState.Open;

	public async Task<TwitchSessionWelcome> ConnectAsync(Uri uri, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_socket is not null)
		{
			throw new InvalidOperationException("This EventSub client already ran a session.");
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
		var socket = await _connector(uri, linked.Token).ConfigureAwait(false);
		_socket = socket;
		_readLoop = Task.Run(() => ReadLoopAsync(socket, _cts.Token), CancellationToken.None);

		var welcome = await _welcome.Task.WaitAsync(_timeout, linked.Token).ConfigureAwait(false);

		return welcome ??
			throw new TwitchEventSubException(
				$"The EventSub connection to {uri} ended before the welcome: {_endReason ?? "unknown"}");
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
			_logger.Debug(ex, "Error while closing the EventSub socket");
		}

		if (_readLoop is { } loop)
		{
			try
			{
				await loop.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "EventSub read loop ended with an error");
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
		_cts.Dispose();
		FailWelcome("disposed");
		RaiseDisconnected("disposed");
	}

	private static async Task<WebSocket> ConnectClientWebSocketAsync(Uri uri, CancellationToken cancellationToken)
	{
		var socket = new ClientWebSocket();

		socket.Options.KeepAliveInterval = TimeSpan.Zero;

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

	private static string? ReadString(JsonElement element, string property)
		=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private static TwitchEventSubMessage? ParseMessage(JsonElement root)
	{
		if (!root.TryGetProperty("metadata", out var metadata) || metadata.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var messageType = ReadString(metadata, "message_type");
		if (messageType is null)
		{
			return null;
		}

		var payload = root.TryGetProperty("payload", out var payloadElement)
			? payloadElement.Clone()
			: default;

		return new TwitchEventSubMessage(ReadString(metadata, "message_id") ?? string.Empty,
			messageType,
			ParseTimestamp(ReadString(metadata, "message_timestamp")),
			ReadString(metadata, "subscription_type"),
			ReadString(metadata, "subscription_version"),
			payload);
	}

	private static DateTimeOffset ParseTimestamp(string? value)
		=> DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var parsed)
			? parsed
			: DateTimeOffset.UtcNow;

	private static TwitchSessionWelcome? ParseWelcome(JsonElement payload)
	{
		if (payload.ValueKind != JsonValueKind.Object ||
			!payload.TryGetProperty("session", out var session) ||
			session.ValueKind != JsonValueKind.Object)
		{
			return null;
		}

		var sessionId = ReadString(session, "id");
		if (sessionId is null)
		{
			return null;
		}

		var keepalive = session.TryGetProperty("keepalive_timeout_seconds", out var value) &&
			value.ValueKind == JsonValueKind.Number &&
			value.TryGetInt32(out var seconds)
				? TimeSpan.FromSeconds(seconds)
				: TimeSpan.FromSeconds(30);

		return new TwitchSessionWelcome(sessionId, keepalive);
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
					reason = DescribeClose(result);
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
			_logger.Debug(ex, "EventSub read loop failed");
		}

		FailWelcome(reason);
		RaiseDisconnected(reason);
	}

	private static string DescribeClose(WebSocketReceiveResult result)
	{
		var code = result.CloseStatus is { } status
			? ((int)status).ToString(CultureInfo.InvariantCulture)
			: "unknown";

		return string.IsNullOrEmpty(result.CloseStatusDescription)
			? code
			: $"{code} ({result.CloseStatusDescription})";
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
			_logger.Debug(ex, "Discarded a malformed EventSub message");
			return;
		}

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object || ParseMessage(root) is not { } message)
			{
				return;
			}

			if (message.MessageType == TwitchEventSubMessageTypes.Welcome)
			{
				if (ParseWelcome(message.Payload) is { } welcome)
				{
					_welcome.TrySetResult(welcome);
				}

				return;
			}

			MessageReceived?.Invoke(this, message);
		}
	}

	private void FailWelcome(string? reason)
	{
		_endReason = reason;
		_welcome.TrySetResult(null);
	}

	private void RaiseDisconnected(string? reason)
	{
		if (Interlocked.Exchange(ref _disconnectRaised, 1) == 0)
		{
			Disconnected?.Invoke(this, reason);
		}
	}
}
