using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.StreamlabsDesktop.Protocol;

internal sealed class StreamlabsJsonRpcClient : IStreamlabsClient
{
	internal const int MaxMessageBytes = 8 * 1024 * 1024;

	private static readonly ILogger _logger =
		IntegrationLog.For<StreamlabsJsonRpcClient>(StreamlabsDesktopIntegration.IntegrationId);

	private static readonly TimeSpan _defaultRequestTimeout = TimeSpan.FromSeconds(10);

	private readonly Func<Uri, CancellationToken, Task<WebSocket>> _connector;
	private readonly TimeSpan _requestTimeout;

	private readonly ConcurrentDictionary<long, TaskCompletionSource<StreamlabsFrame>> _pending = new();

	private readonly ConcurrentDictionary<string, TaskCompletionSource<StreamlabsFrame>> _promises =
		new(StringComparer.Ordinal);

	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly CancellationTokenSource _cts = new();

	private WebSocket? _socket;
	private Task? _readLoop;
	private long _nextId;
	private int _disconnectRaised;
	private bool _disposed;

	public StreamlabsJsonRpcClient()
		: this(ConnectClientWebSocketAsync)
	{
	}

	internal StreamlabsJsonRpcClient(
		Func<Uri, CancellationToken, Task<WebSocket>> connector,
		TimeSpan? requestTimeout = null)
	{
		_connector = connector;
		_requestTimeout = requestTimeout ?? _defaultRequestTimeout;
	}

	public event EventHandler<StreamlabsEvent>? EventReceived;

	public event EventHandler<string?>? Disconnected;

	public bool IsConnected => _socket?.State == WebSocketState.Open;

	public async Task ConnectAsync(Uri uri, string token, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_socket is not null)
		{
			throw new InvalidOperationException("This Streamlabs Desktop client already ran a session.");
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
		var socket = await _connector(uri, linked.Token).ConfigureAwait(false);
		_socket = socket;
		_readLoop = Task.Run(() => ReadLoopAsync(socket, _cts.Token), CancellationToken.None);

		JsonElement result;
		try
		{
			result = await SendAndAwaitAsync(id => StreamlabsRpcFrames.Auth(id, token), linked.Token)
				.ConfigureAwait(false);
		}
		catch (StreamlabsRpcException ex)
		{
			throw new StreamlabsAuthenticationException("Streamlabs Desktop rejected the API token.",
				ex);
		}

		if (result.ValueKind != JsonValueKind.True)
		{
			throw new StreamlabsAuthenticationException("Streamlabs Desktop rejected the API token.");
		}
	}

	public async Task<JsonElement> InvokeAsync(
		string resource,
		string method,
		IReadOnlyList<object?>? args,
		CancellationToken cancellationToken)
	{
		if (!IsConnected)
		{
			throw new StreamlabsRpcException(string.Create(CultureInfo.InvariantCulture,
				$"Streamlabs Desktop is not connected; {resource}.{method} was not sent."));
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
		return await SendAndAwaitAsync(id => StreamlabsRpcFrames.Request(id, resource, method, args), linked.Token)
			.ConfigureAwait(false);
	}

	public async Task<string> SubscribeAsync(
		string service,
		string observable,
		CancellationToken cancellationToken)
	{
		if (!IsConnected)
		{
			throw new StreamlabsRpcException(string.Create(CultureInfo.InvariantCulture,
				$"Streamlabs Desktop is not connected; {service}.{observable} was not subscribed."));
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
		var frame = await SendAndAwaitFrameAsync(id => StreamlabsRpcFrames.Subscribe(id, service, observable),
				linked.Token)
			.ConfigureAwait(false);

		if (frame.Kind != StreamlabsFrameKind.Subscription || frame.ResourceId is not { } resourceId)
		{
			throw new StreamlabsRpcException(string.Create(CultureInfo.InvariantCulture,
				$"Streamlabs Desktop did not return a subscription for {service}.{observable}."));
		}

		return resourceId;
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
				await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, closeTimeout.Token)
					.ConfigureAwait(false);
			}
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Error while closing the Streamlabs Desktop socket");
		}

		if (_readLoop is { } loop)
		{
			try
			{
				await loop.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "The Streamlabs Desktop read loop ended with an error");
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
		EndSession("disposed");
	}

	private static async Task<WebSocket> ConnectClientWebSocketAsync(Uri uri, CancellationToken cancellationToken)
	{
		var socket = new ClientWebSocket();
		try
		{
			socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
			await socket.ConnectAsync(uri, cancellationToken).ConfigureAwait(false);
			return socket;
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	private async Task<JsonElement> SendAndAwaitAsync(
		Func<long, string> buildRequest,
		CancellationToken cancellationToken)
	{
		var frame = await SendAndAwaitFrameAsync(buildRequest, cancellationToken).ConfigureAwait(false);
		return frame.Data;
	}

	private async Task<StreamlabsFrame> SendAndAwaitFrameAsync(
		Func<long, string> buildRequest,
		CancellationToken cancellationToken)
	{
		var id = Interlocked.Increment(ref _nextId);
		var completion = new TaskCompletionSource<StreamlabsFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[id] = completion;

		try
		{
			await SendRawAsync(buildRequest(id), cancellationToken).ConfigureAwait(false);
			return await completion.Task.WaitAsync(_requestTimeout, cancellationToken).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			throw new StreamlabsRpcException(string.Create(CultureInfo.InvariantCulture,
				$"Streamlabs Desktop did not answer within {_requestTimeout.TotalSeconds} seconds."));
		}
		finally
		{
			_pending.TryRemove(id, out _);
			RemovePromise(completion);
		}
	}

	private void RemovePromise(TaskCompletionSource<StreamlabsFrame> completion)
	{
		foreach (var pair in _promises)
		{
			if (ReferenceEquals(pair.Value, completion))
			{
				_promises.TryRemove(pair.Key, out _);
			}
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
					reason = result.CloseStatusDescription ?? "closed by Streamlabs Desktop";
					break;
				}

				if (result.MessageType == WebSocketMessageType.Binary)
				{
					if (result.EndOfMessage)
					{
						message.SetLength(0);
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
				Dispatch(text);
			}
		}
		catch (OperationCanceledException)
		{
			reason = "disconnected";
		}
		catch (Exception ex)
		{
			reason = ex.Message;
			_logger.Debug(ex, "The Streamlabs Desktop read loop failed");
		}

		EndSession(reason);
	}

	private void Dispatch(string text)
	{
		if (!StreamlabsRpcFrames.TryParse(text, out var frame))
		{
			_logger.Debug("Discarded a malformed Streamlabs Desktop frame");
			return;
		}

		switch (frame.Kind)
		{
			case StreamlabsFrameKind.Error:
				if (frame.Id is { } errorId && _pending.TryRemove(errorId, out var failing))
				{
					failing.TrySetException(new StreamlabsRpcException(
						frame.ErrorMessage ?? "Streamlabs Desktop refused the request.",
						frame.ErrorCode));
				}

				break;

			case StreamlabsFrameKind.Subscription:
				HandleSubscription(frame);
				break;

			case StreamlabsFrameKind.Event:
				HandleEvent(frame);
				break;

			case StreamlabsFrameKind.Result:
				if (frame.Id is { } resultId && _pending.TryRemove(resultId, out var waiting))
				{
					waiting.TrySetResult(frame);
				}

				break;

			default:
				break;
		}
	}

	private void HandleSubscription(StreamlabsFrame frame)
	{
		if (frame.Id is not { } id || !_pending.TryRemove(id, out var waiting))
		{
			return;
		}

		if (!frame.IsPromise || frame.ResourceId is not { } resourceId)
		{
			waiting.TrySetResult(frame);
			return;
		}

		_promises[resourceId] = waiting;
	}

	private void HandleEvent(StreamlabsFrame frame)
	{
		if (frame.ResourceId is not { } resourceId)
		{
			return;
		}

		if (frame.IsPromise)
		{
			if (!_promises.TryRemove(resourceId, out var waiting))
			{
				return;
			}

			if (frame.IsRejected)
			{
				waiting.TrySetException(new StreamlabsRpcException(
					StreamlabsModelReader.ReadString(StreamlabsModelReader.Property(frame.Data, "message")) ??
					"Streamlabs Desktop rejected the request."));
			}
			else
			{
				waiting.TrySetResult(frame);
			}

			return;
		}

		EventReceived?.Invoke(this, new StreamlabsEvent(resourceId, frame.Data));
	}

	private async Task SendRawAsync(string text, CancellationToken cancellationToken)
	{
		var socket = _socket;
		if (socket is null || socket.State != WebSocketState.Open)
		{
			throw new StreamlabsRpcException("The Streamlabs Desktop socket is not open.");
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

	private void EndSession(string? reason)
	{
		var closed = new StreamlabsRpcException(string.Create(CultureInfo.InvariantCulture,
			$"The Streamlabs Desktop connection closed ({reason ?? "unknown"})."));

		foreach (var key in _pending.Keys)
		{
			if (_pending.TryRemove(key, out var waiting))
			{
				waiting.TrySetException(closed);
			}
		}

		foreach (var key in _promises.Keys)
		{
			if (_promises.TryRemove(key, out var waiting))
			{
				waiting.TrySetException(closed);
			}
		}

		if (Interlocked.Exchange(ref _disconnectRaised, 1) == 0)
		{
			Disconnected?.Invoke(this, reason);
		}
	}
}
