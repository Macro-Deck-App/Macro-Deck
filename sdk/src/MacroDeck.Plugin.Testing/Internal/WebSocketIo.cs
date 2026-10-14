using System.Net.WebSockets;

namespace MacroDeck.Plugin.Testing.Internal;

/// <summary>Reads one complete WebSocket text message, accumulating frames until <c>EndOfMessage</c>.</summary>
internal static class WebSocketIo
{
	private const int FrameBufferSize = 32 * 1024;

	/// <summary>Returns the message bytes, an empty array for a message over <paramref name="maxBytes" />, or
	/// null when the peer closed the socket. Cancelling <paramref name="cancellationToken" /> aborts the socket and throws.</summary>
	public static async Task<byte[]?> ReceiveOneMessageAsync(
		WebSocket socket,
		int maxBytes,
		CancellationToken cancellationToken)
	{
		using var buffer = new MemoryStream();
		var chunk = new byte[FrameBufferSize];
		var oversized = false;

		while (true)
		{
			WebSocketReceiveResult received;

			try
			{
				received = await socket.ReceiveAsync(chunk, cancellationToken).ConfigureAwait(false);
			}
			catch (WebSocketException)
			{
				return null;
			}

			if (received.MessageType == WebSocketMessageType.Close)
			{
				return null;
			}

			if (!oversized)
			{
				buffer.Write(chunk, 0, received.Count);
				oversized = buffer.Length > maxBytes;
			}

			if (received.EndOfMessage)
			{
				// Like the real host, an oversized message is read to its end and then fails to parse.
				return oversized ? [] : buffer.ToArray();
			}
		}
	}
}
