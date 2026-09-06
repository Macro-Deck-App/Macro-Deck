using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

internal sealed class WebSocketPair : IDisposable
{
	private readonly TcpClient _clientSocket;
	private readonly TcpClient _serverSocket;

	private WebSocketPair(TcpClient clientSocket, TcpClient serverSocket)
	{
		_clientSocket = clientSocket;
		_serverSocket = serverSocket;

		Client = WebSocket.CreateFromStream(clientSocket.GetStream(),
			isServer: false,
			subProtocol: null,
			keepAliveInterval: TimeSpan.FromMinutes(1));

		Server = WebSocket.CreateFromStream(serverSocket.GetStream(),
			isServer: true,
			subProtocol: null,
			keepAliveInterval: TimeSpan.FromMinutes(1));
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
		=> Server.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)),
			WebSocketMessageType.Text,
			endOfMessage: true,
			CancellationToken.None);

	public async Task<string> ReceiveAsync()
	{
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

	public void Dispose()
	{
		Client.Dispose();
		Server.Dispose();
		_clientSocket.Dispose();
		_serverSocket.Dispose();
	}
}
