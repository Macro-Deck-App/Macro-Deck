using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeckHost.WebSockets;
using Serilog;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Ui;

public static class UiWebSocketEndpointExtensions
{
	public static IEndpointConventionBuilder MapUiWebSocket(this IEndpointRouteBuilder endpoints)
		=> endpoints.Map(UiWebSocketProtocol.Path,
				static context =>
					context.RequestServices.GetRequiredService<UiWebSocketEndpoint>().HandleAsync(context))
			.AllowAnonymous();
}

public sealed class UiWebSocketEndpoint(
	IUiWebSocketTickets tickets,
	WebSocketUiTransport transport,
	IServiceScopeFactory scopes,
	IHostApplicationLifetime lifetime,
	TimeProvider timeProvider)
{
	private const int QueueLimit = 256;

	// Reported to the client when an outbound envelope will not fit the protocol's size limit.
	internal const string OversizedCode = "message_too_large";

	private static readonly TimeSpan SendDeadline = TimeSpan.FromSeconds(30);
	private static readonly ILogger _logger = Log.ForContext<UiWebSocketEndpoint>();

	public async Task HandleAsync(HttpContext context)
	{
		if (!context.WebSockets.IsWebSocketRequest ||
			!tickets.TryRedeem(context, context.Request.Query["ticket"], out var principal))
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		if (!context.WebSockets.WebSocketRequestedProtocols.Contains(UiWebSocketProtocol.SubProtocol,
			StringComparer.Ordinal))
		{
			context.Response.StatusCode = StatusCodes.Status400BadRequest;
			return;
		}

		using var socket = await context.WebSockets.AcceptWebSocketAsync(UiWebSocketProtocol.SubProtocol);
		using var cancellation
			= CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted, lifetime.ApplicationStopping);
		var connectionId = Guid.NewGuid().ToString("N");
		var outbound = Channel.CreateBounded<UiWebSocketEnvelope>(new BoundedChannelOptions(QueueLimit)
		{
			FullMode = BoundedChannelFullMode.Wait,
			SingleReader = true
		});
		var active = new ConcurrentDictionary<string, CancellationTokenSource>(StringComparer.Ordinal);
		var operations = new ConcurrentDictionary<Guid, Task>();
		var writer = WriteAsync(socket, outbound.Reader, cancellation, timeProvider);
		var lastInbound = timeProvider.GetUtcNow().UtcTicks;
		var heartbeat = HeartbeatAsync(socket,
			outbound.Writer,
			() => new DateTimeOffset(Interlocked.Read(ref lastInbound), TimeSpan.Zero),
			cancellation);
		UiWebSocketDispatcher? dispatcher = null;
		IServiceScope? connectionScope = null;
		var dispatcherStarted = false;

		ValueTask<bool> Enqueue(UiWebSocketEnvelope message, CancellationToken token)
		{
			if (token.IsCancellationRequested || cancellation.IsCancellationRequested)
			{
				return ValueTask.FromResult(false);
			}

			if (outbound.Writer.TryWrite(message))
			{
				return ValueTask.FromResult(true);
			}

			cancellation.Cancel();
			socket.Abort();
			return ValueTask.FromResult(false);
		}

		try
		{
			var hello = await ReceiveAsync(socket, cancellation.Token);
			Interlocked.Exchange(ref lastInbound, timeProvider.GetUtcNow().UtcTicks);
			if (hello is not { Kind: "hello", ProtocolVersion: UiWebSocketProtocol.Version } ||
				!ValidId(hello.Id) ||
				!ValidId(hello.CorrelationId))
			{
				await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "Invalid hello", CancellationToken.None);
				return;
			}

			connectionScope = scopes.CreateScope();
			dispatcher = ActivatorUtilities.CreateInstance<UiWebSocketDispatcher>(connectionScope.ServiceProvider,
				connectionId,
				principal,
				cancellation.Token,
				(Action)cancellation.Cancel);
			dispatcherStarted = true;
			if (!await dispatcher.ConnectedAsync())
			{
				await CloseAsync(socket, WebSocketCloseStatus.PolicyViolation, "Disconnected", CancellationToken.None);
				return;
			}

			await Enqueue(
				new UiWebSocketEnvelope(UiWebSocketProtocol.Version, "welcome", null, null, hello.Id, null, null),
				cancellation.Token);
			transport.Add(connectionId, Enqueue);
			await dispatcher.ActivateAsync();

			while (!cancellation.IsCancellationRequested && socket.State == WebSocketState.Open)
			{
				var envelope = await ReceiveAsync(socket, cancellation.Token);
				Interlocked.Exchange(ref lastInbound, timeProvider.GetUtcNow().UtcTicks);
				if (envelope is null)
				{
					break;
				}

				if (envelope.ProtocolVersion != UiWebSocketProtocol.Version ||
					!ValidId(envelope.Id) ||
					!ValidId(envelope.CorrelationId))
				{
					await CloseAsync(socket,
						WebSocketCloseStatus.PolicyViolation,
						"Invalid envelope",
						CancellationToken.None);
					break;
				}

				switch (envelope.Kind)
				{
					case "ping":
						await Enqueue(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
								"pong",
								null,
								null,
								envelope.Id,
								null,
								null),
							cancellation.Token);
						break;
					case "pong":
						break;
					case "cancel" when envelope.CorrelationId is { } cancelId:
						if (active.TryGetValue(cancelId, out var request))
						{
							Cancel(request);
						}

						break;
					case "goodbye":
						await CloseAsync(socket, WebSocketCloseStatus.NormalClosure, "Goodbye", CancellationToken.None);
						return;
					case "request" when envelope.Type is not null && envelope.Id is { } id:
						if (!TryStartDispatch(id,
							dispatcher,
							envelope,
							true,
							active,
							operations,
							Enqueue,
							cancellation.Token))
						{
							await Enqueue(Error(envelope, "busy"), cancellation.Token);
						}

						break;
					case "message" when envelope.Type is not null:
						var operationId = $"message:{Guid.NewGuid():N}";
						if (!TryStartDispatch(operationId,
							dispatcher,
							envelope,
							false,
							active,
							operations,
							Enqueue,
							cancellation.Token))
						{
							await Enqueue(Error(envelope, "busy"), cancellation.Token);
						}

						break;
					default:
						await CloseAsync(socket,
							WebSocketCloseStatus.PolicyViolation,
							"Invalid envelope",
							CancellationToken.None);
						return;
				}
			}
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch (WebSocketException)
		{
		}
		catch (Exception exception) when (exception is JsonException or InvalidDataException)
		{
			await CloseAsync(socket, WebSocketCloseStatus.InvalidPayloadData, "Invalid JSON", CancellationToken.None);
		}
		finally
		{
			foreach (var request in active.Values)
			{
				Cancel(request);
			}

			cancellation.Cancel();
			await IgnoreFailures(Task.WhenAll(operations.Values));
			if (dispatcherStarted && dispatcher is not null)
			{
				await IgnoreFailures(dispatcher.DisconnectedAsync());
			}

			dispatcher?.Dispose();
			connectionScope?.Dispose();
			transport.Remove(connectionId);
			outbound.Writer.TryComplete();
			await IgnoreFailures(Task.WhenAll(writer, heartbeat));
		}
	}

	private static bool TryStartDispatch(string operationId,
		UiWebSocketDispatcher dispatcher,
		UiWebSocketEnvelope request,
		bool respond,
		ConcurrentDictionary<string, CancellationTokenSource> active,
		ConcurrentDictionary<Guid, Task> operations,
		Func<UiWebSocketEnvelope, CancellationToken, ValueTask<bool>> send,
		CancellationToken connectionCancellation)
	{
		if (active.Count >= QueueLimit)
		{
			return false;
		}

		var cancellation = CancellationTokenSource.CreateLinkedTokenSource(connectionCancellation);
		if (!active.TryAdd(operationId, cancellation))
		{
			cancellation.Dispose();
			return false;
		}

		var operation = DispatchAsync(dispatcher, operationId, request, respond, active, send);
		var operationKey = Guid.NewGuid();
		operations[operationKey] = operation;
		_ = operation.ContinueWith(static (completed, state) =>
			{
				_ = completed.Exception;
				var (pending, key) = ((ConcurrentDictionary<Guid, Task>, Guid))state!;
				pending.TryRemove(key, out _);
			},
			(operations, operationKey),
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
		return true;
	}

	private static async Task DispatchAsync(UiWebSocketDispatcher dispatcher,
		string operationId,
		UiWebSocketEnvelope request,
		bool respond,
		ConcurrentDictionary<string, CancellationTokenSource> active,
		Func<UiWebSocketEnvelope, CancellationToken, ValueTask<bool>> send)
	{
		var cancellation = active[operationId];
		try
		{
			var payload = request.Payload is JsonElement element ? element : (JsonElement?)null;
			var result = await dispatcher.DispatchAsync(request.Type!, payload, cancellation.Token);
			if (respond && !cancellation.IsCancellationRequested)
			{
				await send(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
						"response",
						request.Type,
						null,
						request.Id,
						result,
						null),
					CancellationToken.None);
			}
		}
		catch (UiWebSocketDispatchException exception)
		{
			await send(Error(request, exception.Code), CancellationToken.None);
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch
		{
			await send(Error(request, "failed"), CancellationToken.None);
		}
		finally
		{
			if (active.TryRemove(operationId, out var source))
			{
				source.Dispose();
			}
		}
	}

	private static UiWebSocketEnvelope Error(UiWebSocketEnvelope request, string code)
		=> new(UiWebSocketProtocol.Version, "error", request.Type, null, request.Id, null, new UiWebSocketError(code));

	private static bool ValidId(string? value) => value is null or { Length: <= 128 };

	internal static async Task WriteAsync(WebSocket socket,
		ChannelReader<UiWebSocketEnvelope> reader,
		CancellationTokenSource cancellation,
		TimeProvider timeProvider)
	{
		try
		{
			await foreach (var envelope in reader.ReadAllAsync(cancellation.Token))
			{
				var bytes = JsonSerializer.SerializeToUtf8Bytes(envelope, UiWebSocketProtocol.Json);
				if (bytes.Length > UiWebSocketProtocol.MaxMessageBytes)
				{
					if (await ReportOversized(socket, envelope, bytes.Length, timeProvider, cancellation))
					{
						continue;
					}

					cancellation.Cancel();
					return;
				}

				await WebSocketMessageIO.SendTextAsync(socket, bytes, SendDeadline, timeProvider, cancellation.Token);
			}
		}
		catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
		{
		}
		catch
		{
			cancellation.Cancel();
			socket.Abort();
		}
	}

	// Reports an envelope too large to send and says whether the session may continue. This used to abort
	// the socket silently, leaving a client waiting forever for a message it was never told was dropped.
	private static async Task<bool> ReportOversized(WebSocket socket,
		UiWebSocketEnvelope envelope,
		int size,
		TimeProvider timeProvider,
		CancellationTokenSource cancellation)
	{
		_logger.Error(
			"Outbound UI message {Kind}/{Type} is {Size} bytes, over the {Limit} byte limit, and was not sent",
			envelope.Kind,
			envelope.Type ?? "-",
			size,
			UiWebSocketProtocol.MaxMessageBytes);

		// Only a correlated envelope can be reported without ending the session: a client matches an error
		// to its pending request by correlationId and ignores one carrying none (websocket-transport.ts).
		if (envelope.CorrelationId is not null)
		{
			// Built here rather than through Error, which reads the id of an inbound request: what
			// correlates an outbound envelope to that request is its own CorrelationId.
			var error = new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
				"error",
				envelope.Type,
				null,
				envelope.CorrelationId,
				null,
				new UiWebSocketError(OversizedCode));

			var bytes = JsonSerializer.SerializeToUtf8Bytes(error, UiWebSocketProtocol.Json);
			if (bytes.Length <= UiWebSocketProtocol.MaxMessageBytes)
			{
				await WebSocketMessageIO.SendTextAsync(socket, bytes, SendDeadline, timeProvider, cancellation.Token);
				return true;
			}
		}

		await CloseAsync(socket, WebSocketCloseStatus.MessageTooBig, "Message too large", CancellationToken.None);
		return false;
	}

	private async Task HeartbeatAsync(WebSocket socket,
		ChannelWriter<UiWebSocketEnvelope> writer,
		Func<DateTimeOffset> lastInbound,
		CancellationTokenSource cancellation)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromSeconds(20), timeProvider);
		while (await timer.WaitForNextTickAsync(cancellation.Token))
		{
			if (timeProvider.GetUtcNow() - lastInbound() >= TimeSpan.FromSeconds(60))
			{
				cancellation.Cancel();
				socket.Abort();
				return;
			}

			if (!writer.TryWrite(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
				"ping",
				null,
				null,
				null,
				null,
				null)))
			{
				cancellation.Cancel();
				socket.Abort();
				return;
			}
		}
	}

	private static async Task<UiWebSocketEnvelope?> ReceiveAsync(WebSocket socket, CancellationToken cancellationToken)
	{
		var bytes = await WebSocketMessageIO.ReceiveAsync(socket,
			UiWebSocketProtocol.MaxMessageBytes,
			requireText: true,
			rejectOversized: true,
			cancellationToken);
		if (bytes is null)
		{
			return null;
		}

		return JsonSerializer.Deserialize<UiWebSocketEnvelope>(bytes, UiWebSocketProtocol.Json) ??
			throw new JsonException();
	}

	private static async Task CloseAsync(WebSocket socket,
		WebSocketCloseStatus status,
		string reason,
		CancellationToken cancellationToken)
	{
		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		deadline.CancelAfter(SendDeadline);
		try
		{
			if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
			{
				await socket.CloseOutputAsync(status, reason, deadline.Token);
			}
		}
		catch (Exception exception) when (exception is WebSocketException or OperationCanceledException)
		{
		}
	}

	private static async Task IgnoreFailures(Task task)
	{
		try
		{
			await task;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
		}
	}

	private static void Cancel(CancellationTokenSource source)
	{
		try
		{
			source.Cancel();
		}
		catch (ObjectDisposedException)
		{
		}
	}
}

public sealed class UiWebSocketDispatchException(string code) : Exception
{
	public string Code { get; } = code;
}
