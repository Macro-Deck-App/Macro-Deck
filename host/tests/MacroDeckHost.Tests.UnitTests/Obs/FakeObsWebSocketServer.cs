using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json.Nodes;

namespace MacroDeckHost.Tests.UnitTests.Obs;

internal enum ObsServerFault
{
	None,
	NoHello,
	SilentAfterIdentified,
	AbortMidMessage
}

internal sealed class FakeObsWebSocketServer : IAsyncDisposable
{
	private readonly HttpListener _listener = new();
	private readonly CancellationTokenSource _cts = new();
	private readonly Func<string, int?> _statusFor;
	private readonly ObsServerFault _fault;
	private readonly Action<int>? _onSession;
	private readonly Task _serving;
	private int _sessions;

	public FakeObsWebSocketServer(
		Func<string, int?> statusFor,
		ObsServerFault fault = ObsServerFault.None,
		Action<int>? onSession = null)
	{
		_statusFor = statusFor;
		_fault = fault;
		_onSession = onSession;
		Port = FreePort();
		_listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
		_listener.Start();
		_serving = ServeAsync();
	}

	public int Port { get; }

	public string Url => $"ws://127.0.0.1:{Port}";

	private readonly List<string> _requests = [];

	public IReadOnlyList<string> Requests
	{
		get
		{
			lock (_requests)
			{
				return _requests.ToList();
			}
		}
	}

	public async ValueTask DisposeAsync()
	{
		await _cts.CancelAsync();
		_listener.Close();
		try
		{
			await _serving;
		}
		catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException)
		{
		}

		_cts.Dispose();
	}

	private static int FreePort()
	{
		var probe = new TcpListener(IPAddress.Loopback, 0);
		probe.Start();
		var port = ((IPEndPoint)probe.LocalEndpoint).Port;
		probe.Stop();
		return port;
	}

	private async Task ServeAsync()
	{
		while (!_cts.IsCancellationRequested)
		{
			var context = await _listener.GetContextAsync();
			if (!context.Request.IsWebSocketRequest)
			{
				context.Response.StatusCode = 400;
				context.Response.Close();
				continue;
			}

			_onSession?.Invoke(Interlocked.Increment(ref _sessions));
			var socket = (await context.AcceptWebSocketAsync(null)).WebSocket;
			_ = Task.Run(() => SessionAsync(context, socket));
		}
	}

	private async Task SessionAsync(HttpListenerContext context, WebSocket socket)
	{
		if (_fault is ObsServerFault.NoHello)
		{
			await DrainAsync(socket);
			return;
		}

		await SendAsync(socket,
			new JsonObject
			{
				["op"] = 0,
				["d"] = new JsonObject { ["obsWebSocketVersion"] = "5.4.0", ["rpcVersion"] = 1 }
			});

		while (socket.State == WebSocketState.Open && !_cts.IsCancellationRequested)
		{
			var message = await ReceiveAsync(socket);
			if (message is null)
			{
				return;
			}

			var op = message["op"]!.GetValue<int>();
			if (op == 1)
			{
				await SendAsync(socket,
					new JsonObject
					{
						["op"] = 2,
						["d"] = new JsonObject { ["negotiatedRpcVersion"] = 1 }
					});

				if (_fault is ObsServerFault.SilentAfterIdentified)
				{
					await DrainAsync(socket);
					return;
				}

				if (_fault is ObsServerFault.AbortMidMessage)
				{
					// HttpListener exposes no raw stream, so a non-final fragment stands in for a cut frame.
					await socket.SendAsync(Encoding.UTF8.GetBytes("""{"op":5,"d":{"eventType":"""),
						WebSocketMessageType.Text,
						false,
						_cts.Token);
					context.Response.Abort();
					return;
				}
			}
			else if (op == 6)
			{
				var requestType = message["d"]!["requestType"]!.GetValue<string>();
				var requestId = message["d"]!["requestId"]!.GetValue<string>();
				lock (_requests)
				{
					_requests.Add(requestType);
				}

				if (_statusFor(requestType) is not { } code)
				{
					continue;
				}

				await SendAsync(socket,
					new JsonObject
					{
						["op"] = 7,
						["d"] = new JsonObject
						{
							["requestType"] = requestType,
							["requestId"] = requestId,
							["requestStatus"] = new JsonObject { ["result"] = code == 100, ["code"] = code },
							["responseData"] = new JsonObject()
						}
					});
			}
		}
	}

	private async Task DrainAsync(WebSocket socket)
	{
		var buffer = new byte[16 * 1024];
		try
		{
			while (socket.State == WebSocketState.Open)
			{
				var result = await socket.ReceiveAsync(buffer, _cts.Token);
				if (result.MessageType == WebSocketMessageType.Close)
				{
					return;
				}
			}
		}
		catch (Exception ex) when (ex is WebSocketException or OperationCanceledException or ObjectDisposedException)
		{
		}
	}

	private async Task SendAsync(WebSocket socket, JsonObject message)
	{
		await socket.SendAsync(Encoding.UTF8.GetBytes(message.ToJsonString()),
			WebSocketMessageType.Text,
			true,
			_cts.Token);
	}

	private async Task<JsonNode?> ReceiveAsync(WebSocket socket)
	{
		var buffer = new byte[16 * 1024];
		using var stream = new MemoryStream();
		WebSocketReceiveResult result;
		do
		{
			result = await socket.ReceiveAsync(buffer, _cts.Token);
			if (result.MessageType == WebSocketMessageType.Close)
			{
				return null;
			}

			stream.Write(buffer, 0, result.Count);
		} while (!result.EndOfMessage);

		return JsonNode.Parse(stream.ToArray());
	}
}
