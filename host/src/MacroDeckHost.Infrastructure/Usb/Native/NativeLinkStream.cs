using System.Threading.Channels;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal sealed class NativeLinkStream : IDisposable
{
	private readonly NativeLink _link;
	private readonly CancellationTokenSource _abort = new();
	private readonly Lock _creditGate = new();

	private readonly Channel<byte[]> _inbound = Channel.CreateUnbounded<byte[]>(
		new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

	private long _sendCredit = LinkProtocol.InitialWindow;
	private TaskCompletionSource? _creditWaiter;
	private int _inboundPending;
	private long _pendingGrant;
	private int _windowQueued;

	public NativeLinkStream(NativeLink link, ushort id)
	{
		_link = link;
		Id = id;
	}

	public ushort Id { get; }

	public Task Completion { get; private set; } = Task.CompletedTask;

	public void Start() => Completion = TaskObservation.Settle(Task.Run(async () =>
	{
		try
		{
			await RunAsync();
		}
		finally
		{
			Dispose();
		}
	}), _link.Logger);

	public bool Receive(byte[] payload)
	{
		if (Interlocked.Add(ref _inboundPending, payload.Length) > LinkProtocol.InitialWindow)
		{
			return false;
		}

		_inbound.Writer.TryWrite(payload);
		return true;
	}

	public void Grant(uint credit)
	{
		TaskCompletionSource? waiter;
		lock (_creditGate)
		{
			_sendCredit = Math.Min(_sendCredit + credit, LinkProtocol.InitialWindow);
			waiter = _creditWaiter;
			_creditWaiter = null;
		}

		waiter?.TrySetResult();
	}

	public void CloseFromPeer() => _inbound.Writer.TryComplete();

	public uint TakeGrant()
	{
		Volatile.Write(ref _windowQueued, 0);
		return (uint)Interlocked.Exchange(ref _pendingGrant, 0);
	}

	public void Abort()
	{
		_inbound.Writer.TryComplete();
		try
		{
			TaskObservation.Observe(_abort.CancelAsync());
		}
		catch (ObjectDisposedException)
		{
		}
	}

	public void Dispose() => _abort.Dispose();

	private async Task RunAsync()
	{
		Stream? socket;
		try
		{
			socket = await _link.Dialer.DialAsync(_link.DeviceKey, _abort.Token);
		}
		catch (Exception ex)
		{
			_link.Logger.Debug(ex, "USB link {DeviceKey} could not bridge stream {Stream}", _link.DeviceKey, Id);
			socket = null;
		}

		if (socket is null)
		{
			_link.EndStream(this);
			return;
		}

		await using (socket)
		{
			using var stopReading = CancellationTokenSource.CreateLinkedTokenSource(_abort.Token);
			var toSocket = PumpToSocketAsync(socket, _abort.Token);
			var fromSocket = PumpFromSocketAsync(socket, stopReading.Token);
			var finished = await Task.WhenAny(toSocket, fromSocket);
			_link.EndStream(this);
			if (finished == toSocket)
			{
				await stopReading.CancelAsync();
			}

			await Task.WhenAll(TaskObservation.Settle(toSocket, _link.Logger),
				TaskObservation.Settle(fromSocket, _link.Logger));
		}
	}

	private async Task PumpToSocketAsync(Stream socket, CancellationToken cancellationToken)
	{
		await foreach (var chunk in _inbound.Reader.ReadAllAsync(cancellationToken))
		{
			await socket.WriteAsync(chunk, cancellationToken);
			Interlocked.Add(ref _inboundPending, -chunk.Length);
			Interlocked.Add(ref _pendingGrant, chunk.Length);
			if (Interlocked.Exchange(ref _windowQueued, 1) == 0)
			{
				_link.RequestWindow(this);
			}
		}
	}

	private async Task PumpFromSocketAsync(Stream socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[LinkProtocol.MaxPayloadLength];
		while (true)
		{
			var allowed = await WaitForCreditAsync(cancellationToken);
			var read = await socket.ReadAsync(buffer.AsMemory(0, allowed), cancellationToken);
			if (read == 0)
			{
				return;
			}

			lock (_creditGate)
			{
				_sendCredit -= read;
			}

			await _link.AcquireDataSlotAsync(cancellationToken);
			if (!_link.TrySendData(this, buffer.AsMemory(0, read)))
			{
				_link.ReleaseDataSlot();
				return;
			}
		}
	}

	private async Task<int> WaitForCreditAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			Task waiting;
			lock (_creditGate)
			{
				if (_sendCredit > 0)
				{
					return (int)Math.Min(_sendCredit, LinkProtocol.MaxPayloadLength);
				}

				_creditWaiter ??= new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
				waiting = _creditWaiter.Task;
			}

			await waiting.WaitAsync(cancellationToken);
		}
	}
}
