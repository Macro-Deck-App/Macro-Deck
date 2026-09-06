using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

internal sealed class PluginWebSocketConnectionPair : IDisposable
{
	private readonly TcpClient _hostSocket;
	private readonly TcpClient _pluginSocket;

	private PluginWebSocketConnectionPair(TcpClient hostSocket, TcpClient pluginSocket)
	{
		_hostSocket = hostSocket;
		_pluginSocket = pluginSocket;

		Host = WebSocket.CreateFromStream(hostSocket.GetStream(),
			isServer: true,
			subProtocol: null,
			keepAliveInterval: TimeSpan.FromMinutes(1));

		Plugin = WebSocket.CreateFromStream(pluginSocket.GetStream(),
			isServer: false,
			subProtocol: null,
			keepAliveInterval: TimeSpan.FromMinutes(1));
	}

	public WebSocket Host { get; }

	public WebSocket Plugin { get; }

	public static async Task<PluginWebSocketConnectionPair> CreateAsync()
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

			return new PluginWebSocketConnectionPair(accepted, connecting);
		}
		finally
		{
			listener.Stop();
		}
	}

	public void Dispose()
	{
		Host.Dispose();
		Plugin.Dispose();
		_hostSocket.Dispose();
		_pluginSocket.Dispose();
	}
}
