using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace MacroDeckHost.Tests.UnitTests.TestSupport;

internal sealed class WebSocketPair : IDisposable
{
	private readonly TcpClient _clientSocket;
	private readonly TcpClient _serverSocket;
	private readonly CancellationTokenSource _disposed = new();
	private volatile bool _halfOpen;

	private WebSocketPair(TcpClient clientSocket, TcpClient serverSocket)
	{
		_clientSocket = clientSocket;
		_serverSocket = serverSocket;

		Client = WebSocket.CreateFromStream(clientSocket.GetStream(),
			false,
			null,
			TimeSpan.FromMinutes(1));

		Server = WebSocket.CreateFromStream(serverSocket.GetStream(),
			true,
			null,
			TimeSpan.FromMinutes(1));
	}

	public WebSocket Client { get; }

	public WebSocket Server { get; }

	public static async Task<WebSocketPair> CreateAsync()
	{
		var listener = new TcpListener(IPAddress.Loopback, 0);
		listener.Start();
		try
		{
			var port = ((IPEndPoint)listener.LocalEndpoint).Port;
			var connecting = new TcpClient();
			var connect = connecting.ConnectAsync(IPAddress.Loopback, port);
			var accepted = await listener.AcceptTcpClientAsync();
			await connect;

			return new WebSocketPair(connecting, accepted);
		}
		finally
		{
			listener.Stop();
		}
	}

	public Task SendAsync(string json)
	{
		if (_halfOpen)
		{
			return Task.CompletedTask;
		}

		return Server.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
			WebSocketMessageType.Text,
			true,
			CancellationToken.None);
	}

	public async Task<string> ReceiveAsync()
	{
		if (_halfOpen)
		{
			return await WaitUntilDisposedAsync();
		}

		var buffer = new byte[8192];
		using var message = new MemoryStream();

		while (true)
		{
			var result = await Server.ReceiveAsync(buffer, CancellationToken.None);
			if (result.MessageType == WebSocketMessageType.Close)
			{
				return string.Empty;
			}

			message.Write(buffer, 0, result.Count);
			if (result.EndOfMessage)
			{
				return Encoding.UTF8.GetString(message.GetBuffer(), 0, (int)message.Length);
			}
		}
	}

	public void Break()
	{
		_serverSocket.Client.Close(0);
		_serverSocket.Dispose();
	}

	public void StallHandshake()
	{
		_ = Task.Run(async () =>
		{
			var buffer = new byte[8192];
			try
			{
				while (!_disposed.IsCancellationRequested)
				{
					var result = await Server.ReceiveAsync(buffer, _disposed.Token);
					if (result.MessageType == WebSocketMessageType.Close)
					{
						return;
					}
				}
			}
			catch (Exception ex) when (ex is OperationCanceledException
				or WebSocketException
				or ObjectDisposedException)
			{
			}
		});
	}

	public void HalfOpen()
	{
		_halfOpen = true;
	}

	public async Task AbortMidMessageAsync(int announcedBytes = 64, int sentBytes = 8)
	{
		var header = announcedBytes < 126
			? new byte[] { 0x81, (byte)announcedBytes }
			: [0x81, 126, (byte)(announcedBytes >> 8), (byte)announcedBytes];
		var payload = Encoding.ASCII.GetBytes(new string('x', sentBytes));

		var stream = _serverSocket.GetStream();
		await stream.WriteAsync(header);
		await stream.WriteAsync(payload);
		await stream.FlushAsync();

		// A reset that overtakes the partial frame would only look like a plain drop to the client.
		await Task.Delay(50);
		_serverSocket.Client.LingerState = new LingerOption(true, 0);
		_serverSocket.Client.Close();
	}

	public void Dispose()
	{
		_disposed.Cancel();
		Client.Dispose();
		Server.Dispose();
		_clientSocket.Dispose();
		_serverSocket.Dispose();
		_disposed.Dispose();
	}

	private async Task<string> WaitUntilDisposedAsync()
	{
		try
		{
			await Task.Delay(Timeout.Infinite, _disposed.Token);
		}
		catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException)
		{
		}

		return string.Empty;
	}
}
