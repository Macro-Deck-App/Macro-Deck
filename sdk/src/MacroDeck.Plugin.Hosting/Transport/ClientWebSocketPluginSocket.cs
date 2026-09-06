using System.Buffers;
using System.Net.WebSockets;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>The real socket: <see cref="ClientWebSocket" /> against the host's WebSocket endpoint.</summary>
internal sealed class ClientWebSocketPluginSocket(ClientWebSocket socket, int maxMessageBytes) : IPluginSocket
{
	private const int ReceiveChunkBytes = 16 * 1024;

	public int? CloseCode => socket.CloseStatus is null ? null : (int)socket.CloseStatus;

	public string? CloseDescription => socket.CloseStatusDescription;

	/// <summary>
	/// Connects to the host's WebSocket endpoint.
	///
	/// <para>
	/// The session token goes in the <c>Authorization</c> header and nowhere else. Not a cookie, which
	/// a browser would attach automatically and so would open the endpoint to cross-site hijacking;
	/// not the query string, which lands in logs. The plugin secret is never sent here at all - it
	/// bought the session token and its job is done.
	/// </para>
	/// </summary>
	public static async Task<ClientWebSocketPluginSocket> ConnectAsync(
		Uri hostUrl,
		string sessionToken,
		int maxMessageBytes,
		CancellationToken cancellationToken)
	{
		var socket = new ClientWebSocket();

		try
		{
			socket.Options.AddSubProtocol(ProtocolConstants.WebSocketSubProtocol);
			socket.Options.SetRequestHeader(PluginAuthDefaults.AuthorizationHeaderName,
				$"{PluginAuthDefaults.BearerScheme} {sessionToken}");

			var endpoint = new Uri(ToWebSocketScheme(hostUrl), ProtocolConstants.WebSocketPath);
			await socket.ConnectAsync(endpoint, cancellationToken);

			return new ClientWebSocketPluginSocket(socket, maxMessageBytes);
		}
		catch
		{
			socket.Dispose();
			throw;
		}
	}

	public Task SendAsync(ReadOnlyMemory<byte> utf8, CancellationToken cancellationToken)
		=> socket.SendAsync(utf8, WebSocketMessageType.Text, endOfMessage: true, cancellationToken).AsTask();

	public async Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken)
	{
		var buffer = ArrayPool<byte>.Shared.Rent(ReceiveChunkBytes);
		var accumulated = new MemoryStream();

		try
		{
			while (true)
			{
				var received = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);

				if (received.MessageType == WebSocketMessageType.Close)
				{
					return null;
				}

				// Checked as the message accumulates, so an oversize body is refused on the frame that
				// crosses the limit rather than after the whole thing has been held in memory.
				if (accumulated.Length + received.Count > maxMessageBytes)
				{
					await DrainAsync(buffer, cancellationToken);
					throw new PluginMessageTooLargeException(maxMessageBytes);
				}

				await accumulated.WriteAsync(buffer.AsMemory(0, received.Count), cancellationToken);

				if (received.EndOfMessage)
				{
					return accumulated.ToArray();
				}
			}
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
			await accumulated.DisposeAsync();
		}
	}

	public async Task CloseOutputAsync(
		WebSocketCloseStatus status,
		string? description,
		CancellationToken cancellationToken)
	{
		if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
		{
			// CloseOutputAsync rather than CloseAsync: the latter waits for the peer's close frame by
			// receiving, which throws when the receive loop already has a receive outstanding.
			await socket.CloseOutputAsync(status, description, cancellationToken);
		}
	}

	public Task AbortAsync()
	{
		socket.Abort();
		return Task.CompletedTask;
	}

	public ValueTask DisposeAsync()
	{
		socket.Dispose();
		return ValueTask.CompletedTask;
	}

	/// <summary>
	/// Reads the rest of an oversize message without keeping it, so the stream stays framed and the
	/// next message is still readable if the caller decides to continue.
	/// </summary>
	private async Task DrainAsync(byte[] buffer, CancellationToken cancellationToken)
	{
		try
		{
			ValueWebSocketReceiveResult received;
			do
			{
				received = await socket.ReceiveAsync(buffer.AsMemory(), cancellationToken);
			} while (!received.EndOfMessage && received.MessageType != WebSocketMessageType.Close);
		}
		catch (WebSocketException)
		{
			// The connection is being torn down anyway; the oversize message is the error worth
			// reporting, not whatever went wrong while discarding it.
		}
	}

	private static Uri ToWebSocketScheme(Uri hostUrl)
	{
		var builder = new UriBuilder(hostUrl)
		{
			Scheme = string.Equals(hostUrl.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
				? "wss"
				: "ws"
		};

		return builder.Uri;
	}
}
