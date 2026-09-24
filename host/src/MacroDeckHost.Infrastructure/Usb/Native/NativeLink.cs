using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Threading.Channels;
using Serilog.Events;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.Usb.Native;

internal sealed class NativeLink
{
	public const int MaxStreams = 64;
	public const int MaxQueuedDataFrames = 16;
	public const int MaxQueuedFrames = 1024;

	private static readonly TimeSpan _tickInterval = TimeSpan.FromMilliseconds(250);

	private static readonly TimeSpan _writerShutdownGrace = TimeSpan.FromSeconds(1);

	private readonly ILinkCarrier _carrier;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;
	private readonly Lock _gate = new();
	private readonly LinkFrameReader _reader = new();
	private readonly Dictionary<ushort, NativeLinkStream> _streams = [];
	private readonly List<LinkInbound> _tickInbound = [];

	private readonly Channel<Outbound> _outbound = Channel.CreateUnbounded<Outbound>();
	private readonly Channel<bool> _dataSlots = Channel.CreateBounded<bool>(MaxQueuedDataFrames);

	private uint _ownEpoch;
	private uint _peerEpoch;
	private bool _answered;
	private DateTimeOffset _lastSentAt;
	private DateTimeOffset _lastReceivedAt;
	private DateTimeOffset _lastHelloAt;
	private volatile bool _byeReceived;
	private volatile bool _everLinked;
	private volatile bool _writing;
	private bool _helloQueued;
	private bool _ackQueued;

	public NativeLink(ILinkCarrier carrier,
		string deviceKey,
		ILinkStreamDialer dialer,
		TimeProvider time,
		ILogger logger)
	{
		_carrier = carrier;
		DeviceKey = deviceKey;
		Dialer = dialer;
		_time = time;
		_logger = logger.ForContext<NativeLink>();
	}

	public string DeviceKey { get; }

	public ILinkStreamDialer Dialer { get; }

	public bool IsLinked
	{
		get
		{
			lock (_gate)
			{
				return _answered && _peerEpoch != 0 && !_byeReceived;
			}
		}
	}

	public bool EverLinked => _everLinked;

	public bool ByeReceived => _byeReceived;

	internal ILogger Logger => _logger;

	internal int ReadsHandled { get; private set; }

	internal int QueuedFrames => _outbound.Reader.Count;

	internal Task LoopsEnded { get; private set; } = Task.CompletedTask;

	internal int FreeDataSlots => MaxQueuedDataFrames - _dataSlots.Reader.Count;

	public async Task RunAsync(CancellationToken cancellationToken)
	{
		var run = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		lock (_gate)
		{
			var now = _time.GetUtcNow();
			_lastReceivedAt = now;
			Restart(now, "carrier opened", enterSeeking: true);
		}

		var loops = new[] { ReadLoopAsync(run.Token), WriteLoopAsync(run.Token) };
		LoopsEnded = TaskObservation.Settle(Task.WhenAll(loops), _logger);

		using var ticker = _time.CreateTimer(_ => Tick(), null, _tickInterval, _tickInterval);
		try
		{
			var finished = await Task.WhenAny(loops);
			if (finished.Exception?.GetBaseException() is { } failure && !cancellationToken.IsCancellationRequested)
			{
				_logger.Write(_everLinked ? LogEventLevel.Information : LogEventLevel.Debug,
					failure,
					"USB link {DeviceKey} lost",
					DeviceKey);
			}
		}
		finally
		{
			await run.CancelAsync();
			_outbound.Writer.TryComplete();
			List<NativeLinkStream> dropped;
			lock (_gate)
			{
				dropped = DetachStreams();
			}

			Abort(dropped);
			await Task.WhenAny(loops[0]);

			// A write blocked in the carrier is not waited for beyond a grace: the carrier's own disposal
			// waits for it before closing the handle.
			await Task.WhenAny(loops[1], Task.Delay(_writerShutdownGrace, CancellationToken.None));
			await Task.WhenAll(dropped.Select(stream => stream.Completion));

			// A writer left behind after the stop grace still holds run's token, so run is disposed only
			// once both loops have ended.
			_ = LoopsEnded.ContinueWith(_ => run.Dispose(),
				CancellationToken.None,
				TaskContinuationOptions.ExecuteSynchronously,
				TaskScheduler.Default);
		}
	}

	// DATA takes a slot that is only freed once the frame is written, so a carrier that stops accepting
	// writes stops the stream pumps instead of queueing without bound.
	internal ValueTask AcquireDataSlotAsync(CancellationToken cancellationToken)
		=> _dataSlots.Writer.WriteAsync(true, cancellationToken);

	internal void ReleaseDataSlot() => _dataSlots.Reader.TryRead(out _);

	internal bool TrySendData(NativeLinkStream stream, ReadOnlyMemory<byte> payload)
	{
		lock (_gate)
		{
			if (!IsCurrent(stream))
			{
				return false;
			}

			_outbound.Writer.TryWrite(new Outbound(OutboundKind.Data, LinkFrame.Data(stream.Id, payload).Encode(), stream));
			_lastSentAt = _time.GetUtcNow();
			return true;
		}
	}

	internal void RequestWindow(NativeLinkStream stream)
	{
		lock (_gate)
		{
			if (IsCurrent(stream))
			{
				_outbound.Writer.TryWrite(new Outbound(OutboundKind.Window, Stream: stream));
				_lastSentAt = _time.GetUtcNow();
			}
		}
	}

	internal void EndStream(NativeLinkStream stream)
	{
		lock (_gate)
		{
			if (!IsCurrent(stream))
			{
				return;
			}

			_streams.Remove(stream.Id);
			Enqueue(LinkFrame.Close(stream.Id), _time.GetUtcNow());
		}

		stream.Abort();
	}

	private bool IsCurrent(NativeLinkStream stream)
		=> _streams.TryGetValue(stream.Id, out var current) && ReferenceEquals(current, stream);

	private async Task ReadLoopAsync(CancellationToken cancellationToken)
	{
		var buffer = new byte[LinkProtocol.MaxFrameLength];
		var inbound = new List<LinkInbound>();
		while (!cancellationToken.IsCancellationRequested)
		{
			var read = await _carrier.ReadAsync(buffer, cancellationToken);
			if (read <= 0)
			{
				continue;
			}

			inbound.Clear();
			lock (_gate)
			{
				var now = _time.GetUtcNow();
				_lastReceivedAt = now;
				_reader.Append(buffer.AsSpan(0, read), now, inbound);
				Handle(inbound, now);
				ReadsHandled++;
			}
		}
	}

	private async Task WriteLoopAsync(CancellationToken cancellationToken)
	{
		await foreach (var item in _outbound.Reader.ReadAllAsync(cancellationToken))
		{
			var frame = Materialize(item);
			if (frame is null)
			{
				if (item.Kind == OutboundKind.Data)
				{
					ReleaseDataSlot();
				}

				continue;
			}

			using var timeout = new CancellationTokenSource(LinkProtocol.FrameWriteTimeout, _time);
			using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
			try
			{
				_writing = true;
				await _carrier.WriteFrameAsync(frame, linked.Token);
			}
			catch (Exception ex) when (!cancellationToken.IsCancellationRequested &&
				(ex is TimeoutException || (ex is OperationCanceledException && timeout.IsCancellationRequested)))
			{
				// Before the app ever answered, nothing reads the other end yet: the frame is dropped and HELLO
				// keeps being offered on the same carrier instead of reopening it every 15 seconds.
				if (!_everLinked)
				{
					_logger.Debug("USB link {DeviceKey} is waiting for the app to read", DeviceKey);
					continue;
				}

				throw new LinkLostException("A link frame was not written in time.");
			}
			finally
			{
				_writing = false;
				if (item.Kind == OutboundKind.Data)
				{
					ReleaseDataSlot();
				}
			}
		}
	}

	// HELLO, its answer and WINDOW are built when they are written, so repeats while one is queued coalesce
	// and they always carry the current epochs and the credit granted so far.
	private byte[]? Materialize(Outbound item)
	{
		lock (_gate)
		{
			switch (item.Kind)
			{
				case OutboundKind.Hello:
					_helloQueued = false;
					return LinkFrame.Hello(false, _ownEpoch, 0).Encode();
				case OutboundKind.HelloAck:
					_ackQueued = false;
					return LinkFrame.Hello(true, _ownEpoch, _peerEpoch).Encode();
				case OutboundKind.Data:
					return IsCurrent(item.Stream!) ? item.Frame : null;
				case OutboundKind.Window:
					var credit = IsCurrent(item.Stream!) ? item.Stream!.TakeGrant() : 0;
					return credit == 0 ? null : LinkFrame.Window(item.Stream!.Id, credit).Encode();
				default:
					return item.Frame;
			}
		}
	}

	private void Tick()
	{
		lock (_gate)
		{
			var now = _time.GetUtcNow();
			if (_reader.IsStuck(now))
			{
				_tickInbound.Clear();
				_reader.Resync(now, _tickInbound);
				Handle(_tickInbound, now);
			}

			if (now - _lastReceivedAt >= LinkProtocol.PeerTimeout)
			{
				_lastReceivedAt = now;
				Restart(now, "peer silent", enterSeeking: true);
			}

			var nothingQueued = !_writing && _outbound.Reader.Count == 0;
			if (!_answered && nothingQueued && now - _lastHelloAt >= LinkProtocol.HelloInterval)
			{
				SendHello(now);
			}

			if (nothingQueued && now - _lastSentAt >= LinkProtocol.KeepaliveInterval)
			{
				Enqueue(LinkFrame.Window(LinkProtocol.ControlStream, 0), now);
			}
		}
	}

	private void Handle(List<LinkInbound> inbound, DateTimeOffset now)
	{
		foreach (var item in inbound)
		{
			if (item.Resync)
			{
				// The reader already rescanned what it had buffered; seeking again would drop what it found.
				Restart(now, "malformed or incomplete frame", enterSeeking: false);
				continue;
			}

			var frame = item.Frame;
			switch (frame.Type)
			{
				case LinkFrameType.Hello:
					OnHello(LinkHello.Parse(frame), now);
					break;
				case LinkFrameType.Open:
					OnOpen(frame.Stream, now);
					break;
				case LinkFrameType.Data:
					OnData(frame, now);
					break;
				case LinkFrameType.Close:
					if (_streams.Remove(frame.Stream, out var closed))
					{
						closed.CloseFromPeer();
					}

					break;
				case LinkFrameType.Window:
					if (frame.Stream != LinkProtocol.ControlStream && _streams.TryGetValue(frame.Stream, out var granted))
					{
						granted.Grant(frame.Credit);
					}

					break;
				case LinkFrameType.Bye:
					_byeReceived = true;
					_logger.Information("USB link {DeviceKey} closed by the app", DeviceKey);
					Abort(DetachStreams());
					break;
			}
		}
	}

	private void OnHello(LinkHello hello, DateTimeOffset now)
	{
		if (hello.Epoch == 0)
		{
			return;
		}

		if (hello.Epoch != _peerEpoch)
		{
			_peerEpoch = hello.Epoch;
			_byeReceived = false;
			Abort(DetachStreams());
		}

		if (hello.Ack && hello.Echo == _ownEpoch && !_answered)
		{
			_answered = true;
			_everLinked = true;
			_logger.Information("USB link {DeviceKey} established, protocol version {Version}",
				DeviceKey,
				Math.Min(LinkProtocol.Version, hello.MaxVersion));
		}

		if (!hello.Ack && !_ackQueued)
		{
			_ackQueued = true;
			_outbound.Writer.TryWrite(new Outbound(OutboundKind.HelloAck));
			_lastSentAt = now;
		}
	}

	private void OnOpen(ushort id, DateTimeOffset now)
	{
		if (id == LinkProtocol.ControlStream)
		{
			return;
		}

		if (!_streams.ContainsKey(id) && _streams.Count >= MaxStreams)
		{
			Enqueue(LinkFrame.Close(id), now);
			return;
		}

		if (!_answered || _peerEpoch == 0)
		{
			Enqueue(LinkFrame.Close(id), now);
			return;
		}

		if (_streams.Remove(id, out var replaced))
		{
			replaced.Abort();
		}

		var stream = new NativeLinkStream(this, id);
		_streams[id] = stream;
		stream.Start();
	}

	private void OnData(LinkFrame frame, DateTimeOffset now)
	{
		if (!_streams.TryGetValue(frame.Stream, out var stream) || stream.Receive(frame.Payload.ToArray()))
		{
			return;
		}

		_streams.Remove(frame.Stream);
		Enqueue(LinkFrame.Close(frame.Stream), now);
		stream.Abort();
	}

	private void Restart(DateTimeOffset now, string reason, bool enterSeeking)
	{
		_logger.Debug("USB link {DeviceKey} restarting: {Reason}", DeviceKey, reason);
		_ownEpoch = NextEpoch(_ownEpoch);
		_answered = false;
		Abort(DetachStreams());
		if (enterSeeking)
		{
			_reader.EnterSeeking();
		}

		SendHello(now);
	}

	private void SendHello(DateTimeOffset now)
	{
		_lastHelloAt = now;
		_lastSentAt = now;
		if (!_helloQueued)
		{
			_helloQueued = true;
			_outbound.Writer.TryWrite(new Outbound(OutboundKind.Hello));
		}
	}

	private void Enqueue(LinkFrame frame, DateTimeOffset now)
	{
		if (_outbound.Reader.Count >= MaxQueuedFrames)
		{
			_logger.Debug("USB link {DeviceKey} dropped a {Type} frame: the queue is full", DeviceKey, frame.Type);
			return;
		}

		if (_outbound.Writer.TryWrite(new Outbound(OutboundKind.Frame, frame.Encode())))
		{
			_lastSentAt = now;
		}
	}

	private List<NativeLinkStream> DetachStreams()
	{
		var detached = _streams.Values.ToList();
		_streams.Clear();
		return detached;
	}

	private static void Abort(List<NativeLinkStream> streams)
	{
		foreach (var stream in streams)
		{
			stream.Abort();
		}
	}

	private static uint NextEpoch(uint previous)
	{
		Span<byte> bytes = stackalloc byte[4];
		uint epoch;
		do
		{
			RandomNumberGenerator.Fill(bytes);
			epoch = BinaryPrimitives.ReadUInt32BigEndian(bytes);
		} while (epoch == 0 || epoch == previous);

		return epoch;
	}

	private enum OutboundKind
	{
		Frame,

		Data,

		Hello,

		HelloAck,

		Window
	}

	private readonly record struct Outbound(OutboundKind Kind, byte[]? Frame = null, NativeLinkStream? Stream = null);
}
