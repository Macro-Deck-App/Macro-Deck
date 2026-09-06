using System.Net.WebSockets;

namespace MacroDeck.Plugin.Hosting.Transport;

/// <summary>
/// The socket, as everything above it needs to see it: send a message, receive a message, close.
///
/// <para>
/// It exists so the session loops can be tested against an in-memory pair instead of a real port.
/// That is not only convenience - keepalive, timeout and backoff behaviour is only deterministically
/// testable if the socket and the clock are both substitutable.
/// </para>
/// </summary>
internal interface IPluginSocket : IAsyncDisposable
{
	/// <summary>The close code the peer sent, once the socket has closed.</summary>
	int? CloseCode { get; }

	/// <summary>The peer's close description, when it gave one.</summary>
	string? CloseDescription { get; }

	/// <summary>Sends one complete text message.</summary>
	Task SendAsync(ReadOnlyMemory<byte> utf8, CancellationToken cancellationToken);

	/// <summary>
	/// Receives one complete message, reassembling frames. Returns null when the peer closed.
	/// Throws <see cref="PluginMessageTooLargeException" /> when the message exceeds the negotiated
	/// limit, having discarded the rest rather than buffering it.
	/// </summary>
	Task<byte[]?> ReceiveAsync(CancellationToken cancellationToken);

	/// <summary>
	/// Sends a close frame without waiting for the peer's, so it does not collide with a receive
	/// already in flight. Best effort.
	/// </summary>
	Task CloseOutputAsync(WebSocketCloseStatus status, string? description, CancellationToken cancellationToken);

	/// <summary>
	/// Drops the connection without a handshake, for when the peer has stopped answering and there is
	/// nothing left to negotiate with.
	/// </summary>
	Task AbortAsync();
}

/// <summary>
/// A peer sent a message larger than the protocol allows. The body is not buffered - accepting it in
/// order to report its size is the very thing the limit exists to prevent.
/// </summary>
internal sealed class PluginMessageTooLargeException : Exception
{
	public PluginMessageTooLargeException(int limit)
		: base($"The peer sent a message larger than the {limit} byte limit.")
		=> Limit = limit;

	public PluginMessageTooLargeException()
	{
	}

	public PluginMessageTooLargeException(string message)
		: base(message)
	{
	}

	public PluginMessageTooLargeException(string message, Exception innerException)
		: base(message, innerException)
	{
	}

	public int Limit { get; }
}
