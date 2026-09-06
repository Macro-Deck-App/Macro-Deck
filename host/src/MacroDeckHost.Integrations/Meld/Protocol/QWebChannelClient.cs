using System.Collections.Concurrent;
using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.Meld.Protocol;

internal sealed class QWebChannelClient : IQWebChannelClient
{
	internal const int MaxMessageBytes = 4 * 1024 * 1024;

	private static readonly ILogger _logger = IntegrationLog.For<QWebChannelClient>(MeldObjects.IntegrationId);

	private static readonly TimeSpan _defaultInitTimeout = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan _defaultInvokeTimeout = TimeSpan.FromSeconds(5);

	private readonly Func<Uri, CancellationToken, Task<WebSocket>> _connector;
	private readonly TimeSpan _initTimeout;
	private readonly TimeSpan _invokeTimeout;

	private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _pending = new();
	private readonly SemaphoreSlim _sendLock = new(1, 1);
	private readonly CancellationTokenSource _cts = new();

	private Dictionary<string, QWebChannelObjectInfo> _objects = new(StringComparer.Ordinal);

	private WebSocket? _socket;
	private Task? _readLoop;
	private int _nextId;
	private int _disconnectRaised;
	private bool _disposed;

	public QWebChannelClient()
		: this(ConnectClientWebSocketAsync)
	{
	}

	internal QWebChannelClient(
		Func<Uri, CancellationToken, Task<WebSocket>> connector,
		TimeSpan? initTimeout = null,
		TimeSpan? invokeTimeout = null)
	{
		_connector = connector;
		_initTimeout = initTimeout ?? _defaultInitTimeout;
		_invokeTimeout = invokeTimeout ?? _defaultInvokeTimeout;
	}

	public event EventHandler<QWebChannelSignalMessage>? SignalReceived;

	public event EventHandler<QWebChannelPropertyUpdate>? PropertyUpdated;

	public event EventHandler<string?>? Disconnected;

	public bool IsConnected => _socket?.State == WebSocketState.Open;

	public async Task<IReadOnlyDictionary<string, QWebChannelObjectInfo>> ConnectAsync(
		Uri uri,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_socket is not null)
		{
			throw new InvalidOperationException("This QWebChannel client already ran a session.");
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
		var socket = await _connector(uri, linked.Token).ConfigureAwait(false);
		_socket = socket;
		_readLoop = Task.Run(() => ReadLoopAsync(socket, _cts.Token), CancellationToken.None);

		var id = NextId();
		var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[id] = completion;

		try
		{
			await SendAsync(socket,
					new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						["type"] = QWebChannelMessageTypes.Init,
						["id"] = id
					},
					linked.Token)
				.ConfigureAwait(false);

			JsonElement data;
			try
			{
				data = await completion.Task.WaitAsync(_initTimeout, linked.Token).ConfigureAwait(false);
			}
			catch (TimeoutException)
			{
				throw new QWebChannelHandshakeException(
					$"{uri} did not answer the QWebChannel init handshake within {_initTimeout.TotalSeconds}s.");
			}

			if (data.ValueKind != JsonValueKind.Object)
			{
				throw new QWebChannelHandshakeException(
					$"{uri} did not answer the QWebChannel init handshake with object metadata.");
			}

			var objects = new Dictionary<string, QWebChannelObjectInfo>(StringComparer.Ordinal);
			foreach (var property in data.EnumerateObject())
			{
				if (property.Value.ValueKind == JsonValueKind.Object)
				{
					objects[property.Name] = ParseObjectInfo(property.Name, property.Value);
				}
			}

			_objects = objects;
		}
		finally
		{
			_pending.TryRemove(id, out _);
		}

		await SendIdleAsync().ConfigureAwait(false);

		return _objects;
	}

	public async Task<JsonElement> InvokeAsync(
		string objectName,
		string method,
		IReadOnlyList<object?> args,
		CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		var socket = _socket;
		if (socket is null || socket.State != WebSocketState.Open)
		{
			throw new QWebChannelException($"Cannot invoke '{method}': not connected to Meld Studio.");
		}

		if (!_objects.TryGetValue(objectName, out var info) || !info.Methods.TryGetValue(method, out var methodIndex))
		{
			throw new QWebChannelException($"Meld Studio does not expose '{objectName}.{method}'.");
		}

		var id = NextId();
		var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
		_pending[id] = completion;

		try
		{
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
			await SendAsync(socket,
					new Dictionary<string, object?>(StringComparer.Ordinal)
					{
						["type"] = QWebChannelMessageTypes.InvokeMethod,
						["object"] = objectName,
						["method"] = methodIndex,
						["args"] = args,
						["id"] = id
					},
					linked.Token)
				.ConfigureAwait(false);

			try
			{
				return await completion.Task.WaitAsync(_invokeTimeout, linked.Token).ConfigureAwait(false);
			}
			catch (TimeoutException)
			{
				throw new QWebChannelException(
					$"Meld Studio did not answer '{objectName}.{method}' within {_invokeTimeout.TotalSeconds}s.");
			}
		}
		finally
		{
			_pending.TryRemove(id, out _);
		}
	}

	public async Task ConnectToSignalAsync(string objectName, string signal, CancellationToken cancellationToken)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		var socket = _socket;
		if (socket is null || socket.State != WebSocketState.Open)
		{
			throw new QWebChannelException($"Cannot subscribe to '{signal}': not connected to Meld Studio.");
		}

		if (!_objects.TryGetValue(objectName, out var info) || !info.Signals.TryGetValue(signal, out var signalIndex))
		{
			throw new QWebChannelException($"Meld Studio does not expose the '{objectName}.{signal}' signal.");
		}

		using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
		await SendAsync(socket,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["type"] = QWebChannelMessageTypes.ConnectToSignal,
					["object"] = objectName,
					["signal"] = signalIndex
				},
				linked.Token)
			.ConfigureAwait(false);
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
			_logger.Debug(ex, "Error while closing the Meld Studio socket");
		}

		if (_readLoop is { } loop)
		{
			try
			{
				await loop.ConfigureAwait(false);
			}
			catch (Exception ex)
			{
				_logger.Debug(ex, "Meld Studio read loop ended with an error");
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

	private static QWebChannelObjectInfo ParseObjectInfo(string name, JsonElement classInfo)
	{
		var methods = new Dictionary<string, int>(StringComparer.Ordinal);
		if (classInfo.TryGetProperty("methods", out var methodsElement) &&
			methodsElement.ValueKind == JsonValueKind.Array)
		{
			foreach (var entry in methodsElement.EnumerateArray())
			{
				if (!TryReadNameIndexPair(entry, out var methodName, out var index) || methodName.EndsWith(')'))
				{
					continue;
				}

				methods[methodName] = index;
			}
		}

		var signalNameToIndex = new Dictionary<string, int>(StringComparer.Ordinal);
		var signalIndexToName = new Dictionary<int, string>();
		if (classInfo.TryGetProperty("signals", out var signalsElement) &&
			signalsElement.ValueKind == JsonValueKind.Array)
		{
			foreach (var entry in signalsElement.EnumerateArray())
			{
				if (!TryReadNameIndexPair(entry, out var signalName, out var index))
				{
					continue;
				}

				signalNameToIndex[signalName] = index;
				signalIndexToName[index] = signalName;
			}
		}

		var propertyNames = new Dictionary<int, string>();
		var initialProperties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		if (classInfo.TryGetProperty("properties", out var propertiesElement) &&
			propertiesElement.ValueKind == JsonValueKind.Array)
		{
			foreach (var entry in propertiesElement.EnumerateArray())
			{
				if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 4)
				{
					continue;
				}

				if (entry[0].ValueKind != JsonValueKind.Number || !entry[0].TryGetInt32(out var propertyIndex))
				{
					continue;
				}

				if (entry[1].ValueKind != JsonValueKind.String || entry[1].GetString() is not { } propertyName)
				{
					continue;
				}

				propertyNames[propertyIndex] = propertyName;
				initialProperties[propertyName] = entry[3].Clone();
			}
		}

		return new QWebChannelObjectInfo(name,
			methods,
			signalNameToIndex,
			signalIndexToName,
			propertyNames,
			initialProperties);
	}

	private static bool TryReadNameIndexPair(JsonElement entry, out string name, out int index)
	{
		name = string.Empty;
		index = 0;

		if (entry.ValueKind != JsonValueKind.Array || entry.GetArrayLength() < 2)
		{
			return false;
		}

		if (entry[0].ValueKind != JsonValueKind.String || entry[0].GetString() is not { } entryName)
		{
			return false;
		}

		if (entry[1].ValueKind != JsonValueKind.Number || !entry[1].TryGetInt32(out var entryIndex))
		{
			return false;
		}

		name = entryName;
		index = entryIndex;
		return true;
	}

	private static string? ReadString(JsonElement element, string property)
		=> element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
			? value.GetString()
			: null;

	private int NextId() => Interlocked.Increment(ref _nextId);

	private async Task SendAsync(WebSocket socket,
		Dictionary<string, object?> body,
		CancellationToken cancellationToken)
	{
		var bytes = JsonSerializer.SerializeToUtf8Bytes(body, QWebChannelJson.Options);

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

	private async Task SendIdleAsync()
	{
		var socket = _socket;
		if (socket is null || socket.State != WebSocketState.Open)
		{
			return;
		}

		try
		{
			await SendAsync(socket,
					new Dictionary<string, object?>(StringComparer.Ordinal) { ["type"] = QWebChannelMessageTypes.Idle },
					_cts.Token)
				.ConfigureAwait(false);
		}
		catch (Exception ex)
		{
			_logger.Debug(ex, "Failed to send the Meld Studio idle credit");
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
					reason = result.CloseStatusDescription ?? "closed by Meld Studio";
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
			_logger.Debug(ex, "Meld Studio read loop failed");
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
			_logger.Debug(ex, "Discarded a malformed Meld Studio message");
			return;
		}

		using (document)
		{
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!root.TryGetProperty("type", out var typeElement) ||
				typeElement.ValueKind != JsonValueKind.Number ||
				!typeElement.TryGetInt32(out var type))
			{
				return;
			}

			switch (type)
			{
				case QWebChannelMessageTypes.Response:
					HandleResponse(root);
					break;
				case QWebChannelMessageTypes.Signal:
					HandleSignal(root);
					break;
				case QWebChannelMessageTypes.PropertyUpdate:
					HandlePropertyUpdate(root);
					break;
				default:
					_logger.Debug("Discarded a Meld Studio message of type {Type}", type);
					break;
			}
		}
	}

	private void HandleResponse(JsonElement root)
	{
		if (!root.TryGetProperty("id", out var idElement) ||
			idElement.ValueKind != JsonValueKind.Number ||
			!idElement.TryGetInt32(out var id) ||
			!_pending.TryRemove(id, out var pending))
		{
			return;
		}

		var data = root.TryGetProperty("data", out var dataElement) ? dataElement.Clone() : default;
		pending.TrySetResult(data);
	}

	private void HandleSignal(JsonElement root)
	{
		var objectName = ReadString(root, "object");
		if (objectName is null ||
			!root.TryGetProperty("signal", out var signalElement) ||
			signalElement.ValueKind != JsonValueKind.Number ||
			!signalElement.TryGetInt32(out var index))
		{
			return;
		}

		if (!_objects.TryGetValue(objectName, out var info) || !info.SignalNames.TryGetValue(index, out var signalName))
		{
			_logger.Debug("Dropped a Meld Studio signal from unknown object/index {Object}/{Index}", objectName, index);
			return;
		}

		var args = new List<JsonElement>();
		if (root.TryGetProperty("args", out var argsElement) && argsElement.ValueKind == JsonValueKind.Array)
		{
			foreach (var argument in argsElement.EnumerateArray())
			{
				args.Add(argument.Clone());
			}
		}

		SignalReceived?.Invoke(this, new QWebChannelSignalMessage(objectName, signalName, args));
	}

	private void HandlePropertyUpdate(JsonElement root)
	{
		try
		{
			if (root.TryGetProperty("data", out var dataElement) && dataElement.ValueKind == JsonValueKind.Array)
			{
				foreach (var entry in dataElement.EnumerateArray())
				{
					try
					{
						DispatchPropertyUpdateEntry(entry);
					}
					catch (Exception ex)
					{
						_logger.Debug(ex, "A Meld Studio property update handler failed");
					}
				}
			}
		}
		finally
		{
			_ = SendIdleAsync();
		}
	}

	private void DispatchPropertyUpdateEntry(JsonElement entry)
	{
		if (entry.ValueKind != JsonValueKind.Object)
		{
			return;
		}

		var objectName = ReadString(entry, "object");
		if (objectName is null ||
			!entry.TryGetProperty("properties", out var propertiesElement) ||
			propertiesElement.ValueKind != JsonValueKind.Object)
		{
			return;
		}

		if (!_objects.TryGetValue(objectName, out var info))
		{
			_logger.Debug("Dropped a Meld Studio property update for unknown object {Object}", objectName);
			return;
		}

		var resolved = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
		foreach (var property in propertiesElement.EnumerateObject())
		{
			if (int.TryParse(property.Name, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) &&
				info.PropertyNames.TryGetValue(index, out var propertyName))
			{
				resolved[propertyName] = property.Value.Clone();
			}
		}

		PropertyUpdated?.Invoke(this, new QWebChannelPropertyUpdate(objectName, resolved));
	}

	private void FailPending(string? reason)
	{
		foreach (var (id, completion) in _pending)
		{
			if (_pending.TryRemove(id, out _))
			{
				completion.TrySetException(
					new QWebChannelException($"Meld Studio connection ended: {reason ?? "unknown"}"));
			}
		}
	}

	private void RaiseDisconnected(string? reason)
	{
		if (Interlocked.Exchange(ref _disconnectRaised, 1) == 0)
		{
			Disconnected?.Invoke(this, reason);
		}
	}
}
