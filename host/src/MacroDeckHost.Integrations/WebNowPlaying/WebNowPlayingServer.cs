using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using MacroDeck.Sdk.Logging;
using Serilog;

namespace MacroDeckHost.Integrations.WebNowPlaying;

internal sealed class WebNowPlayingConnection(WebSocket socket) : IDisposable
{
	private readonly SemaphoreSlim _sending = new(1, 1);

	public WebSocket Socket { get; } = socket;

	public async Task SendAsync(string text, CancellationToken cancellationToken)
	{
		await _sending.WaitAsync(cancellationToken);
		try
		{
			await Socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, cancellationToken);
		}
		finally
		{
			_sending.Release();
		}
	}

	public void Dispose()
	{
		Socket.Dispose();
		_sending.Dispose();
	}
}

internal sealed class WebNowPlayingServer : IDisposable
{
	public const int DefaultPort = 8698;

	internal const int MaxTextMessageBytes = 64 * 1024;

	internal const int MaxBinaryMessageBytes = 8 * 1024 * 1024;

	private const int MaxRequestHeadBytes = 8 * 1024;
	private const string WebSocketAcceptGuid = "258EAFA5-E914-47DA-95CA-C5AB0DC85B11";

	private static readonly TimeSpan _upgradeTimeout = TimeSpan.FromSeconds(5);
	private static readonly string[] _extensionSchemes = ["chrome-extension", "moz-extension", "safari-web-extension"];

	private static readonly ILogger _logger =
		IntegrationLog.For<WebNowPlayingServer>(WebNowPlayingIntegration.IntegrationId);

	private readonly WebNowPlayingPlayers _players;
	private readonly Action _changed;
	private readonly CancellationTokenSource _stopping = new();
	private readonly ConcurrentDictionary<TcpClient, byte> _clients = new();

	private TcpListener? _listener;
	private Task _acceptLoop = Task.CompletedTask;

	public WebNowPlayingServer(WebNowPlayingPlayers players, Action changed)
	{
		_players = players;
		_changed = changed;
	}

	public int Port => _listener?.LocalEndpoint is IPEndPoint endpoint ? endpoint.Port : 0;

	public bool TryStart(int port)
	{
		var listener = new TcpListener(IPAddress.Loopback, port);

		// Without exclusive use Windows lets another process bind the same port and take the extension's
		// connection, which then carries playback commands.
		if (OperatingSystem.IsWindows())
		{
			listener.ExclusiveAddressUse = true;
		}

		try
		{
			listener.Start();
		}
		catch (SocketException ex)
		{
			_logger.Warning("WebNowPlaying cannot listen on 127.0.0.1:{Port}: {Message}", port, ex.Message);
			return false;
		}

		_listener = listener;
		_acceptLoop = AcceptLoopAsync(listener, _stopping.Token);
		return true;
	}

	public async Task StopAsync()
	{
		Dispose();
		await _acceptLoop;
	}

	public void Dispose()
	{
		if (_stopping.IsCancellationRequested)
		{
			return;
		}

		_stopping.Cancel();
		_listener?.Stop();

		foreach (var client in _clients.Keys)
		{
			client.Dispose();
		}
	}

	private async Task AcceptLoopAsync(TcpListener listener, CancellationToken cancellationToken)
	{
		while (!cancellationToken.IsCancellationRequested)
		{
			TcpClient client;
			try
			{
				client = await listener.AcceptTcpClientAsync(cancellationToken);
			}
			catch (Exception) when (cancellationToken.IsCancellationRequested)
			{
				return;
			}
			catch (SocketException ex)
			{
				_logger.Debug("WebNowPlaying accept failed: {Message}", ex.Message);
				continue;
			}

			_clients[client] = 0;
			_ = ServeAsync(client, cancellationToken);
		}
	}

	private async Task ServeAsync(TcpClient client, CancellationToken cancellationToken)
	{
		WebNowPlayingConnection? connection = null;

		try
		{
			var stream = client.GetStream();
			using (var upgrade = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
			{
				upgrade.CancelAfter(_upgradeTimeout);
				if (!await AcceptUpgradeAsync(stream, upgrade.Token))
				{
					return;
				}
			}

			connection = new WebNowPlayingConnection(WebSocket.CreateFromStream(stream,
				new WebSocketCreationOptions { IsServer = true, KeepAliveInterval = TimeSpan.FromSeconds(30) }));

			await connection.SendAsync(WebNowPlayingProtocol.Handshake, cancellationToken);
			_players.AddConnection(connection);
			_changed();

			await ReceiveAsync(connection, cancellationToken);
		}
		catch (Exception ex) when (ex is OperationCanceledException
			or IOException
			or WebSocketException
			or SocketException
			or ObjectDisposedException)
		{
			_logger.Debug("WebNowPlaying connection ended: {Message}", ex.Message);
		}
		finally
		{
			if (connection is not null)
			{
				_players.RemoveConnection(connection);
				connection.Dispose();
				_changed();
			}

			_clients.TryRemove(client, out _);
			client.Dispose();
		}
	}

	private async Task ReceiveAsync(WebNowPlayingConnection connection, CancellationToken cancellationToken)
	{
		var buffer = new byte[16 * 1024];
		using var message = new MemoryStream();

		while (true)
		{
			var result = await connection.Socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
			if (result.MessageType == WebSocketMessageType.Close)
			{
				await connection.Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);
				return;
			}

			var limit = result.MessageType == WebSocketMessageType.Binary ? MaxBinaryMessageBytes : MaxTextMessageBytes;
			if (message.Length + result.Count > limit)
			{
				_logger.Warning("Closing a WebNowPlaying connection that sent a message over {Limit} bytes", limit);
				await connection.Socket.CloseOutputAsync(WebSocketCloseStatus.MessageTooBig, null, cancellationToken);
				return;
			}

			message.Write(buffer, 0, result.Count);
			if (!result.EndOfMessage)
			{
				continue;
			}

			var content = message.GetBuffer().AsSpan(0, (int)message.Length);
			var changed = result.MessageType == WebSocketMessageType.Binary
				? _players.HandleCover(connection, content)
				: _players.HandleText(connection, Encoding.UTF8.GetString(content));
			message.SetLength(0);

			if (changed)
			{
				_changed();
			}
		}
	}

	[SuppressMessage("Security",
		"CA5350",
		Justification = "RFC 6455 fixes SHA-1 for Sec-WebSocket-Accept; it authenticates nothing.")]
	private static async Task<bool> AcceptUpgradeAsync(NetworkStream stream, CancellationToken cancellationToken)
	{
		var headers = await ReadRequestHeadersAsync(stream, cancellationToken);
		var key = headers?.GetValueOrDefault("Sec-WebSocket-Key");

		if (headers is null ||
			string.IsNullOrWhiteSpace(key) ||
			!string.Equals(headers.GetValueOrDefault("Upgrade"), "websocket", StringComparison.OrdinalIgnoreCase) ||
			!IsAllowedOrigin(headers.GetValueOrDefault("Origin")))
		{
			await stream.WriteAsync(
				"HTTP/1.1 403 Forbidden\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray(),
				cancellationToken);
			return false;
		}

		var accept = Convert.ToBase64String(SHA1.HashData(Encoding.ASCII.GetBytes(key.Trim() + WebSocketAcceptGuid)));
		var response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: Upgrade\r\n" +
			$"Sec-WebSocket-Accept: {accept}\r\n\r\n";
		await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);
		return true;
	}

	// A web page can open a socket to a loopback port too; only the extension's own origins, or a local
	// process that sends none, may feed players in and receive playback commands.
	private static bool IsAllowedOrigin(string? origin)
		=> origin is null ||
			(Uri.TryCreate(origin, UriKind.Absolute, out var uri) &&
				_extensionSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase));

	private static async Task<Dictionary<string, string>?> ReadRequestHeadersAsync(
		NetworkStream stream,
		CancellationToken cancellationToken)
	{
		var head = new List<byte>(512);
		var next = new byte[1];

		while (head.Count < MaxRequestHeadBytes)
		{
			if (await stream.ReadAsync(next, cancellationToken) == 0)
			{
				return null;
			}

			head.Add(next[0]);
			if (head.Count >= 4 && head[^4] == '\r' && head[^3] == '\n' && head[^2] == '\r' && head[^1] == '\n')
			{
				return ParseHeaders(Encoding.ASCII.GetString(head.ToArray()));
			}
		}

		return null;
	}

	private static Dictionary<string, string> ParseHeaders(string head)
	{
		var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		foreach (var line in head.Split("\r\n").Skip(1))
		{
			var separator = line.IndexOf(':');
			if (separator > 0)
			{
				headers[line[..separator].Trim()] = line[(separator + 1)..].Trim();
			}
		}

		return headers;
	}
}
