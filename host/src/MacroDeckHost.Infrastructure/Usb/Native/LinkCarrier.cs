using System.Net.Sockets;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal interface ILinkCarrier : IAsyncDisposable
{
	ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken);

	ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken);
}

internal sealed class LinkLostException : Exception
{
	public LinkLostException(string message)
		: base(message)
	{
	}

	public LinkLostException(string message, Exception innerException)
		: base(message, innerException)
	{
	}
}

internal sealed class SocketLinkCarrier : ILinkCarrier
{
	private readonly Socket _socket;

	public SocketLinkCarrier(Socket socket)
	{
		_socket = socket;
	}

	public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		int read;
		try
		{
			read = await _socket.ReceiveAsync(buffer, SocketFlags.None, cancellationToken);
		}
		catch (SocketException ex)
		{
			throw new LinkLostException("The link socket failed.", ex);
		}

		return read == 0 ? throw new LinkLostException("The link socket was closed.") : read;
	}

	public async ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
	{
		try
		{
			while (!frame.IsEmpty)
			{
				var sent = await _socket.SendAsync(frame, SocketFlags.None, cancellationToken);
				frame = frame[sent..];
			}
		}
		catch (SocketException ex)
		{
			throw new LinkLostException("The link socket failed.", ex);
		}
	}

	public ValueTask DisposeAsync()
	{
		_socket.Dispose();
		return ValueTask.CompletedTask;
	}
}
