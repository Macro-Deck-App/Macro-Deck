using System.Net.WebSockets;

namespace MacroDeck.Plugin.Testing.Internal;

/// <summary>Reads one complete WebSocket text message, accumulating frames until <c>EndOfMessage</c>.</summary>
internal static class WebSocketIo
{
	private const int FrameBufferSize = 32 * 1024;

	/// <summary>Returns the message bytes, or null when the peer closed the socket or sent something over
	/// <paramref name="maxBytes" />. Cancelling <paramref name="cancellationToken" /> aborts the socket and throws.</summary>
	public static async Task<byte[]?> ReceiveOneMessageAsync(WebSocket socket,
		int maxBytes,
		CancellationToken cancellationToken)
	{
		using var buffer = new MemoryStream();
		var chunk = new byte[FrameBufferSize];

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

			buffer.Write(chunk, 0, received.Count);

			if (buffer.Length > maxBytes)
			{
				return null;
			}

			if (received.EndOfMessage)
			{
				return buffer.ToArray();
			}
		}
	}
}
