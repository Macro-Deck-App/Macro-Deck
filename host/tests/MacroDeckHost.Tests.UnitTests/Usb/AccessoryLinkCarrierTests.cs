using MacroDeckHost.Infrastructure.Usb.Native;
using MacroDeckHost.Tests.UnitTests.Delegation;

namespace MacroDeckHost.Tests.UnitTests.Usb;

public class AccessoryLinkCarrierTests
{
	private sealed class ScriptedPipe : IUsbBulkPipe
	{
		private readonly FakeTimeProvider _time;
		private readonly Queue<Func<int, UsbTransferResult>> _writes = new();

		public ScriptedPipe(FakeTimeProvider time)
		{
			_time = time;
		}

		public List<(int Count, int Timeout)> WriteCalls { get; } = [];

		public List<byte> Written { get; } = [];

		public UsbTransferResult NextRead { get; set; } = new(UsbTransferStatus.TimedOut, 0);

		public void ThenWrite(Func<int, UsbTransferResult> result) => _writes.Enqueue(result);

		public UsbTransferResult Read(byte[] buffer, int timeoutMilliseconds) => NextRead;

		public UsbTransferResult Write(byte[] buffer, int offset, int count, int timeoutMilliseconds)
		{
			WriteCalls.Add((count, timeoutMilliseconds));
			var result = _writes.Count > 0
				? _writes.Dequeue()(count)
				: new UsbTransferResult(UsbTransferStatus.Completed, count);
			Written.AddRange(buffer.Skip(offset).Take(result.Transferred));
			if (result.Status == UsbTransferStatus.TimedOut)
			{
				_time.Advance(TimeSpan.FromMilliseconds(timeoutMilliseconds));
			}

			return result;
		}

		public bool Disposed { get; private set; }

		public void Dispose() => Disposed = true;
	}

	[Test]
	public void A_frame_is_one_write_with_the_filler_byte_when_it_would_end_on_a_full_packet()
	{
		var time = new FakeTimeProvider();
		var pipe = new ScriptedPipe(time);
		var carrier = new AccessoryLinkCarrier(pipe, time);
		var frame = LinkFrame.Data(1, new byte[56]).Encode();

		carrier.Write(LinkProtocol.ForUsbTransfer(frame), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(pipe.WriteCalls, Is.EqualTo(new[] { (65, 5000) }));
			Assert.That(pipe.Written.Take(64), Is.EqualTo(frame));
			Assert.That(pipe.Written[64], Is.EqualTo(0xFF));
		});
	}

	[Test]
	public void A_transfer_that_times_out_part_way_continues_with_the_remaining_bytes()
	{
		var time = new FakeTimeProvider();
		var pipe = new ScriptedPipe(time);
		pipe.ThenWrite(_ => new UsbTransferResult(UsbTransferStatus.TimedOut, 512));
		var carrier = new AccessoryLinkCarrier(pipe, time);
		var transfer = LinkProtocol.ForUsbTransfer(LinkFrame.Data(1, new byte[2000]).Encode());

		carrier.Write(transfer, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(pipe.WriteCalls.Select(call => call.Count), Is.EqualTo(new[] { transfer.Length, transfer.Length - 512 }));
			Assert.That(pipe.Written, Is.EqualTo(transfer));
		});
	}

	[Test]
	public void A_frame_not_written_within_fifteen_seconds_loses_the_link()
	{
		var time = new FakeTimeProvider();
		var pipe = new ScriptedPipe(time);
		for (var attempt = 0; attempt < 5; attempt++)
		{
			pipe.ThenWrite(_ => new UsbTransferResult(UsbTransferStatus.TimedOut, 0));
		}

		var carrier = new AccessoryLinkCarrier(pipe, time);

		Assert.Multiple(() =>
		{
			Assert.That(() => carrier.Write(new byte[100], CancellationToken.None), Throws.TypeOf<TimeoutException>());
			Assert.That(pipe.WriteCalls.Select(call => call.Timeout), Is.EqualTo(new[] { 5000, 5000, 5000 }));
		});
	}

	[Test]
	public void A_cancelled_write_stops_between_transfers()
	{
		var time = new FakeTimeProvider();
		var pipe = new ScriptedPipe(time);
		using var stop = new CancellationTokenSource();
		pipe.ThenWrite(_ =>
		{
			stop.Cancel();
			return new UsbTransferResult(UsbTransferStatus.TimedOut, 64);
		});
		var carrier = new AccessoryLinkCarrier(pipe, time);

		Assert.Multiple(() =>
		{
			Assert.That(() => carrier.Write(new byte[1000], stop.Token), Throws.InstanceOf<OperationCanceledException>());
			Assert.That(pipe.WriteCalls, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Disposing_waits_for_a_transfer_in_flight_before_closing_the_pipe()
	{
		var time = new FakeTimeProvider();
		var release = new ManualResetEventSlim();
		var pipe = new ScriptedPipe(time);
		pipe.ThenWrite(count =>
		{
			release.Wait(TimeSpan.FromSeconds(10));
			return new UsbTransferResult(UsbTransferStatus.Completed, count);
		});
		var carrier = new AccessoryLinkCarrier(pipe, time);
		var write = carrier.WriteFrameAsync(new byte[10], CancellationToken.None).AsTask();
		await Eventually.True(() => pipe.WriteCalls.Count == 1, "the transfer started");

		var disposing = carrier.DisposeAsync().AsTask();
		await Task.Delay(50);
		var closedEarly = pipe.Disposed;
		release.Set();
		await disposing.WaitAsync(TimeSpan.FromSeconds(10));
		await write;
		release.Dispose();

		Assert.Multiple(() =>
		{
			Assert.That(closedEarly, Is.False);
			Assert.That(pipe.Disposed, Is.True);
		});
	}

	[Test]
	public void A_failed_transfer_loses_the_link()
	{
		var time = new FakeTimeProvider();
		var pipe = new ScriptedPipe(time);
		pipe.ThenWrite(_ => new UsbTransferResult(UsbTransferStatus.Failed, 0));
		var carrier = new AccessoryLinkCarrier(pipe, time);

		Assert.That(() => carrier.Write(new byte[10], CancellationToken.None), Throws.TypeOf<LinkLostException>());
	}

	[Test]
	public async Task An_empty_read_is_no_data_and_a_failed_read_loses_the_link()
	{
		var time = new FakeTimeProvider();
		var pipe = new ScriptedPipe(time);
		await using var carrier = new AccessoryLinkCarrier(pipe, time);
		var buffer = new byte[LinkProtocol.MaxFrameLength];

		var empty = await carrier.ReadAsync(buffer, CancellationToken.None);
		pipe.NextRead = new UsbTransferResult(UsbTransferStatus.Completed, 0);
		var zeroLengthPacket = await carrier.ReadAsync(buffer, CancellationToken.None);
		pipe.NextRead = new UsbTransferResult(UsbTransferStatus.Failed, 0);

		Assert.Multiple(() =>
		{
			Assert.That(empty, Is.Zero);
			Assert.That(zeroLengthPacket, Is.Zero);
			Assert.That(async () => await carrier.ReadAsync(buffer, CancellationToken.None),
				Throws.TypeOf<LinkLostException>());
		});
	}
}
