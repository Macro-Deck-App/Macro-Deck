namespace MacroDeckHost.Infrastructure.Usb.Native;

internal sealed class AccessoryLinkCarrier : ILinkCarrier
{
	public static readonly TimeSpan TransferTimeout = TimeSpan.FromSeconds(5);

	private const int ReadTimeoutMilliseconds = 500;

	private readonly IUsbBulkPipe _pipe;
	private readonly TimeProvider _time;
	private readonly byte[] _readBuffer = new byte[LinkProtocol.MaxFrameLength];
	private Task _read = Task.CompletedTask;
	private Task _write = Task.CompletedTask;

	public AccessoryLinkCarrier(IUsbBulkPipe pipe, TimeProvider time)
	{
		_pipe = pipe;
		_time = time;
	}

	public async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken)
	{
		var read = Task.Run(() => _pipe.Read(_readBuffer, ReadTimeoutMilliseconds), cancellationToken);
		_read = read;
		var result = await read;
		if (result.Status == UsbTransferStatus.Failed)
		{
			throw new LinkLostException("The accessory read failed.");
		}

		var count = Math.Min(result.Transferred, buffer.Length);
		_readBuffer.AsMemory(0, count).CopyTo(buffer);
		return count;
	}

	public ValueTask WriteFrameAsync(ReadOnlyMemory<byte> frame, CancellationToken cancellationToken)
	{
		var transfer = LinkProtocol.ForUsbTransfer(frame.Span);
		var write = Task.Run(() => Write(transfer, cancellationToken), cancellationToken);
		_write = write;
		return new ValueTask(write);
	}

	// libusb must not close a handle with a transfer still in flight, so closing waits for the last ones.
	public async ValueTask DisposeAsync()
	{
		await TaskObservation.Settle(Task.WhenAll(_read, _write));
		_pipe.Dispose();
	}

	internal void Write(byte[] transfer, CancellationToken cancellationToken)
	{
		var started = _time.GetUtcNow();
		var offset = 0;
		while (offset < transfer.Length)
		{
			cancellationToken.ThrowIfCancellationRequested();
			var remaining = LinkProtocol.FrameWriteTimeout - (_time.GetUtcNow() - started);
			if (remaining <= TimeSpan.Zero)
			{
				throw new TimeoutException("An accessory frame was not written in time.");
			}

			var timeout = remaining < TransferTimeout ? remaining : TransferTimeout;
			var milliseconds = (int)Math.Ceiling(timeout.TotalMilliseconds);
			var result = _pipe.Write(transfer, offset, transfer.Length - offset, milliseconds);
			offset += result.Transferred;
			if (result.Status == UsbTransferStatus.Failed)
			{
				throw new LinkLostException("The accessory write failed.");
			}
		}
	}
}
