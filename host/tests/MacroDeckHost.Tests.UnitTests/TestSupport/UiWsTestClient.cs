using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed record UiWsMessage(
	string Kind,
	string? Type,
	string? Id,
	string? CorrelationId,
	JsonElement? Payload,
	string? ErrorCode);

// Reads in the background so pushes never back up behind the test, and keeps every frame for later
// inspection. A negative assertion is made after a ping round trip, which proves earlier pushes arrived.
internal sealed class UiWsTestClient : IAsyncDisposable
{
	private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

	private readonly WebSocket _socket;
	private readonly Channel<UiWsMessage> _inbound = Channel.CreateUnbounded<UiWsMessage>();
	private readonly List<UiWsMessage> _received = [];
	private readonly Lock _gate = new();
	private readonly TaskCompletionSource<WebSocketCloseStatus?> _closed
		= new(TaskCreationOptions.RunContinuationsAsynchronously);

	private readonly Task _reader;
	private int _nextId;

	public UiWsTestClient(WebSocket socket, bool read = true)
	{
		_socket = socket;
		_reader = read ? ReadLoopAsync() : Task.CompletedTask;
	}

	public WebSocket Socket => _socket;

	public Task<WebSocketCloseStatus?> Closed => _closed.Task;

	public IReadOnlyList<UiWsMessage> Received
	{
		get
		{
			lock (_gate)
			{
				return [.. _received];
			}
		}
	}

	public async Task HelloAsync()
	{
		await SendRawAsync(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
			"hello",
			null,
			"hello-1",
			null,
			null,
			null));
		var welcome = await WaitForAsync(message => message.Kind == "welcome");
		Assert.That(welcome.CorrelationId, Is.EqualTo("hello-1"));
	}

	public async Task<UiWsMessage> RequestAsync(string type, params object?[] arguments)
	{
		var id = StartRequest(type, arguments, out var send);
		await send;
		return await WaitForAsync(message
			=> message.CorrelationId == id && message.Kind is "response" or "error");
	}

	public string StartRequest(string type, object?[] arguments, out Task sent)
	{
		var id = $"request-{Interlocked.Increment(ref _nextId)}";
		sent = SendRawAsync(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
			"request",
			type,
			id,
			null,
			JsonSerializer.SerializeToElement(arguments, UiWebSocketProtocol.Json),
			null));
		return id;
	}

	public async Task SyncAsync()
	{
		var id = $"ping-{Interlocked.Increment(ref _nextId)}";
		await SendRawAsync(new UiWebSocketEnvelope(UiWebSocketProtocol.Version, "ping", null, id, null, null, null));
		await WaitForAsync(message => message.Kind == "pong" && message.CorrelationId == id);
	}

	public async Task SendRawAsync(object envelope)
	{
		var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, UiWebSocketProtocol.Json);
		using var timeout = new CancellationTokenSource(_timeout);
		await _socket.SendAsync(bytes, WebSocketMessageType.Text, true, timeout.Token);
	}

	public async Task<UiWsMessage> WaitForAsync(Func<UiWsMessage, bool> predicate, TimeSpan? timeout = null)
	{
		lock (_gate)
		{
			var existing = _received.FirstOrDefault(predicate);
			if (existing is not null)
			{
				return existing;
			}
		}

		using var cancellation = new CancellationTokenSource(timeout ?? _timeout);
		while (true)
		{
			UiWsMessage message;
			try
			{
				message = await _inbound.Reader.ReadAsync(cancellation.Token);
			}
			catch (ChannelClosedException)
			{
				throw new AssertionException("The UI WebSocket closed before the expected message arrived.");
			}
			catch (OperationCanceledException)
			{
				throw new AssertionException("The expected UI WebSocket message did not arrive in time.");
			}

			if (predicate(message))
			{
				return message;
			}
		}
	}

	public async Task<WebSocketCloseStatus?> WaitForCloseAsync(TimeSpan? timeout = null)
	{
		return await _closed.Task.WaitAsync(timeout ?? _timeout);
	}

	private async Task ReadLoopAsync()
	{
		var buffer = new byte[UiWebSocketProtocol.MaxMessageBytes];
		try
		{
			while (true)
			{
				var count = 0;
				ValueWebSocketReceiveResult result;
				do
				{
					result = await _socket.ReceiveAsync(buffer.AsMemory(count), CancellationToken.None);
					count += result.Count;
				}
				while (!result.EndOfMessage && result.MessageType != WebSocketMessageType.Close);

				if (result.MessageType == WebSocketMessageType.Close)
				{
					_closed.TrySetResult(_socket.CloseStatus);
					return;
				}

				var envelope = JsonSerializer.Deserialize<WireEnvelope>(buffer.AsSpan(0, count), UiWebSocketProtocol.Json)!;
				var message = new UiWsMessage(envelope.Kind,
					envelope.Type,
					envelope.Id,
					envelope.CorrelationId,
					envelope.Payload,
					envelope.Error?.Code);
				lock (_gate)
				{
					_received.Add(message);
				}

				await _inbound.Writer.WriteAsync(message);
			}
		}
		catch (Exception exception) when (exception is WebSocketException or ObjectDisposedException
			or IOException or OperationCanceledException)
		{
			_closed.TrySetResult(null);
		}
		finally
		{
			_inbound.Writer.TryComplete();
		}
	}

	public async ValueTask DisposeAsync()
	{
		_socket.Abort();
		_socket.Dispose();
		try
		{
			await _reader;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
		}
	}

	private sealed record WireEnvelope(
		int ProtocolVersion,
		string Kind,
		string? Type,
		string? Id,
		string? CorrelationId,
		JsonElement? Payload,
		WireError? Error);

	private sealed record WireError(string Code);
}
