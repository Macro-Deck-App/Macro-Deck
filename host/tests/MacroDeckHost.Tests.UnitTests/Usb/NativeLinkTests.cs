using System.Diagnostics;
using System.Text;
using MacroDeckHost.Infrastructure.Usb.Native;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Usb;

public class NativeLinkTests
{
	private const uint AppEpoch = 0xA0A0A0A0;

	private FakeTimeProvider _time = null!;
	private FakeLinkCarrier _carrier = null!;
	private LoopbackTestDialer _dialer = null!;
	private NativeLink _link = null!;
	private CancellationTokenSource _stop = null!;
	private Task _run = null!;

	[SetUp]
	public void StartLink()
	{
		_time = new FakeTimeProvider();
		_carrier = new FakeLinkCarrier();
		_dialer = new LoopbackTestDialer();
		_link = new NativeLink(_carrier, "usb:TEST", _dialer, _time, Logger.None);
		_stop = new CancellationTokenSource();
		_run = _link.RunAsync(_stop.Token);
	}

	[TearDown]
	public async Task StopLink()
	{
		await _stop.CancelAsync();
		await _run.WaitAsync(TimeSpan.FromSeconds(10));
		_stop.Dispose();
		_dialer.Dispose();
		await _carrier.DisposeAsync();
	}

	[Test]
	public async Task The_host_says_hello_and_resends_it_every_second_until_it_is_answered()
	{
		var first = LinkHello.Parse(await _carrier.NextAsync(IsHello(ack: false)));

		_time.Advance(TimeSpan.FromSeconds(1));
		var resent = LinkHello.Parse(await _carrier.NextAsync(IsHello(ack: false), after: 1));

		Assert.Multiple(() =>
		{
			Assert.That(first.Epoch, Is.Not.Zero);
			Assert.That(first.Echo, Is.Zero);
			Assert.That(first.MaxVersion, Is.EqualTo(1));
			Assert.That(resent.Epoch, Is.EqualTo(first.Epoch));
			Assert.That(_link.IsLinked, Is.False);
		});
	}

	[Test]
	public async Task A_hello_from_the_app_is_answered_with_an_ack_echoing_its_epoch_and_the_link_comes_up()
	{
		var hostEpoch = await HandshakeAsync();

		var ack = LinkHello.Parse(await _carrier.NextAsync(frame => IsHello(ack: true)(frame) &&
			LinkHello.Parse(frame).Echo == AppEpoch));

		Assert.Multiple(() =>
		{
			Assert.That(ack.Epoch, Is.EqualTo(hostEpoch));
			Assert.That(_link.IsLinked, Is.True);
		});
	}

	[Test]
	public async Task An_open_before_the_host_epoch_is_answered_is_closed_without_a_dial()
	{
		var hello = LinkHello.Parse(await _carrier.NextAsync(IsHello(ack: false)));
		_carrier.Send(LinkFrame.Hello(false, AppEpoch, 0), LinkFrame.Open(3));
		await _carrier.NextAsync(IsHello(ack: true));
		await _carrier.NextAsync(frame => frame.Type == LinkFrameType.Close && frame.Stream == 3);

		_carrier.Send(LinkFrame.Hello(true, AppEpoch, hello.Epoch), LinkFrame.Open(4));
		await _dialer.AcceptedAsync();

		Assert.That(_dialer.Dials, Is.EqualTo(1));
	}

	[Test]
	public async Task Data_flows_both_ways_and_written_bytes_are_granted_back_as_window_credit()
	{
		await HandshakeAsync();
		_carrier.Send(LinkFrame.Open(3), LinkFrame.Data(3, Encoding.ASCII.GetBytes("GET / HTTP/1.1\r\n")));
		var local = await _dialer.AcceptedAsync();

		var received = await StreamAssertions.ReadExactlyAsync(local, 16);
		await local.WriteAsync(Encoding.ASCII.GetBytes("HTTP/1.1 200 OK"));
		await Eventually.True(() => _carrier.DataOn(3).Length == 15, "the reply came back over the link");
		var window = await _carrier.NextAsync(frame => frame.Type == LinkFrameType.Window && frame.Stream == 3);

		Assert.Multiple(() =>
		{
			Assert.That(Encoding.ASCII.GetString(received), Is.EqualTo("GET / HTTP/1.1\r\n"));
			Assert.That(Encoding.ASCII.GetString(_carrier.DataOn(3)), Is.EqualTo("HTTP/1.1 200 OK"));
			Assert.That(window.Credit, Is.EqualTo(16u));
		});
	}

	[Test]
	public async Task The_host_never_has_more_than_the_stream_credit_in_flight()
	{
		await HandshakeAsync();
		_carrier.Send(LinkFrame.Open(5));
		var local = await _dialer.AcceptedAsync();

		await local.WriteAsync(new byte[300 * 1024]);
		await Eventually.True(() => _carrier.DataOn(5).Length == LinkProtocol.InitialWindow,
			"the initial 256 KiB credit is used up");
		await SyncAsync();
		var beforeGrant = _carrier.DataOn(5).Length;

		_carrier.Send(LinkFrame.Window(5, 10_000));
		await Eventually.True(() => _carrier.DataOn(5).Length == LinkProtocol.InitialWindow + 10_000,
			"the granted credit is used");
		await SyncAsync();

		Assert.Multiple(() =>
		{
			Assert.That(beforeGrant, Is.EqualTo(LinkProtocol.InitialWindow));
			Assert.That(_carrier.DataOn(5), Has.Length.EqualTo(LinkProtocol.InitialWindow + 10_000));
			Assert.That(_carrier.Written.Where(frame => frame.Type == LinkFrameType.Data)
				.All(frame => frame.Payload.Length <= LinkProtocol.MaxPayloadLength), Is.True);
		});
	}

	[Test]
	public async Task A_window_grant_never_raises_the_credit_above_the_protocol_window()
	{
		await HandshakeAsync();
		_carrier.Send(LinkFrame.Open(5), LinkFrame.Window(5, 10_000_000));
		var local = await _dialer.AcceptedAsync();

		var writing = local.WriteAsync(new byte[1024 * 1024]).AsTask();
		await Eventually.True(() => _carrier.DataOn(5).Length == LinkProtocol.InitialWindow,
			"the stream used its credit");
		await SyncAsync();

		Assert.That(_carrier.DataOn(5), Has.Length.EqualTo(LinkProtocol.InitialWindow));
		_dialer.Dispose();
		await writing.ContinueWith(completed => _ = completed.Exception, TaskScheduler.Default);
	}

	[Test]
	public async Task An_open_beyond_the_stream_limit_is_closed_without_a_dial()
	{
		await HandshakeAsync();
		for (ushort id = 1; id <= NativeLink.MaxStreams + 1; id++)
		{
			_carrier.Send(LinkFrame.Open(id));
		}

		var refused = await _carrier.NextAsync(frame => frame.Type == LinkFrameType.Close);
		await Eventually.True(() => _dialer.Dials == NativeLink.MaxStreams, "every stream within the limit dialled");
		await SyncAsync();

		Assert.Multiple(() =>
		{
			Assert.That(refused.Stream, Is.EqualTo(NativeLink.MaxStreams + 1));
			Assert.That(_dialer.Dials, Is.EqualTo(NativeLink.MaxStreams));
		});
	}

	[Test]
	public async Task A_carrier_that_stops_accepting_writes_stops_the_stream_pumps_instead_of_queueing()
	{
		await HandshakeAsync();
		var never = new TaskCompletionSource();
		_carrier.WriteOverride = (_, _) => new ValueTask(never.Task);
		_carrier.Send(LinkFrame.Open(1), LinkFrame.Open(2), LinkFrame.Open(3));
		var writes = new List<Task>();
		foreach (var _ in Enumerable.Range(0, 3))
		{
			var local = await _dialer.AcceptedAsync();
			writes.Add(local.WriteAsync(new byte[LinkProtocol.InitialWindow]).AsTask());
		}

		await Eventually.True(() => _link.FreeDataSlots == 0, "every data slot is taken");
		var queued = _link.QueuedFrames;
		var reads = _link.ReadsHandled;
		for (var hello = 0; hello < 1000; hello++)
		{
			_carrier.Send(LinkFrame.Hello(false, AppEpoch, 0));
		}

		await Eventually.True(() => _link.ReadsHandled >= reads + 1000, "every hello was handled");

		Assert.Multiple(() =>
		{
			Assert.That(queued, Is.LessThanOrEqualTo(NativeLink.MaxQueuedDataFrames));
			Assert.That(_link.QueuedFrames, Is.LessThanOrEqualTo(NativeLink.MaxQueuedDataFrames));
			Assert.That(_link.FreeDataSlots, Is.Zero);
		});
		never.SetResult();
		_dialer.Dispose();
		await Task.WhenAll(writes).ContinueWith(_ => { }, TaskScheduler.Default);
	}

	[Test]
	public async Task Data_queued_for_a_stream_that_was_reset_since_is_never_written_and_frees_its_slot()
	{
		await HandshakeAsync();
		var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_carrier.WriteOverride = (_, _) => new ValueTask(gate.Task);
		var attempts = _carrier.WriteAttempts;
		_carrier.Send(LinkFrame.Hello(false, AppEpoch, 0));
		await Eventually.True(() => _carrier.WriteAttempts > attempts, "a control frame holds the writer");
		_carrier.Send(LinkFrame.Open(3));
		var old = await _dialer.AcceptedAsync();
		await old.WriteAsync("stale"u8.ToArray());
		await Eventually.True(() => _link.FreeDataSlots < NativeLink.MaxQueuedDataFrames, "the data was queued");

		_carrier.Send(LinkFrame.Hello(false, AppEpoch + 1, 0));
		Assert.That(await StreamAssertions.ClosesAsync(old), Is.True);
		_carrier.WriteOverride = null;
		gate.SetResult();
		await SyncAsync(LinkFrame.Hello(false, AppEpoch + 1, 0));

		Assert.Multiple(() =>
		{
			Assert.That(_carrier.DataOn(3), Is.Empty);
			Assert.That(_link.FreeDataSlots, Is.EqualTo(NativeLink.MaxQueuedDataFrames));
		});
	}

	[Test]
	public async Task Data_arriving_while_the_dial_is_in_progress_is_delivered_once_connected()
	{
		await HandshakeAsync();
		_dialer.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_carrier.Send(LinkFrame.Open(6), LinkFrame.Data(6, "early"u8.ToArray()));
		await SyncAsync();

		_dialer.Hold.SetResult();
		var local = await _dialer.AcceptedAsync();

		Assert.That(await StreamAssertions.ReadExactlyAsync(local, 5), Is.EqualTo("early"u8.ToArray()));
	}

	[Test]
	public async Task Data_beyond_the_credit_while_dialing_closes_the_stream()
	{
		await HandshakeAsync();
		_dialer.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		_carrier.Send(LinkFrame.Open(6));
		for (var sent = 0; sent <= LinkProtocol.InitialWindow; sent += LinkProtocol.MaxPayloadLength)
		{
			_carrier.Send(LinkFrame.Data(6, new byte[LinkProtocol.MaxPayloadLength]));
		}

		await _carrier.NextAsync(frame => frame.Type == LinkFrameType.Close && frame.Stream == 6);
		_dialer.Hold.SetResult();
	}

	[Test]
	public async Task An_open_the_host_cannot_bridge_is_answered_with_close()
	{
		await HandshakeAsync();

		_dialer.Refuse = true;
		_carrier.Send(LinkFrame.Open(7));
		await _carrier.NextAsync(frame => frame.Type == LinkFrameType.Close && frame.Stream == 7);

		_dialer.Refuse = false;
		_dialer.Fail = true;
		_carrier.Send(LinkFrame.Open(8));
		await _carrier.NextAsync(frame => frame.Type == LinkFrameType.Close && frame.Stream == 8);
	}

	[Test]
	public async Task A_local_socket_that_closes_sends_close_and_a_close_from_the_app_closes_the_socket()
	{
		await HandshakeAsync();
		_carrier.Send(LinkFrame.Open(9));
		var first = await _dialer.AcceptedAsync();
		first.Close();
		await _carrier.NextAsync(frame => frame.Type == LinkFrameType.Close && frame.Stream == 9);

		_carrier.Send(LinkFrame.Open(10));
		var second = await _dialer.AcceptedAsync();
		_carrier.Send(LinkFrame.Close(10));

		Assert.That(await StreamAssertions.ClosesAsync(second), Is.True);
	}

	[Test]
	public async Task Stream_ids_wrap_past_65535_and_stream_0_never_opens()
	{
		await HandshakeAsync();

		_carrier.Send(LinkFrame.Open(0), LinkFrame.Open(65535), LinkFrame.Open(1));
		await _dialer.AcceptedAsync();
		await _dialer.AcceptedAsync();
		await SyncAsync();

		Assert.That(_dialer.Dials, Is.EqualTo(2));
	}

	[Test]
	public async Task A_repeated_hello_with_the_known_epoch_does_not_reset_open_streams()
	{
		await HandshakeAsync();
		_carrier.Send(LinkFrame.Open(3));
		var local = await _dialer.AcceptedAsync();

		_carrier.Send(LinkFrame.Hello(false, AppEpoch, 0));
		await SyncAsync();
		await local.WriteAsync("still here"u8.ToArray());

		await Eventually.True(() => _carrier.DataOn(3).Length == 10, "the stream kept working");
	}

	[Test]
	public async Task A_new_app_epoch_resets_every_stream_without_close_frames()
	{
		await HandshakeAsync();
		_carrier.Send(LinkFrame.Open(3));
		var local = await _dialer.AcceptedAsync();

		_carrier.Send(LinkFrame.Hello(false, AppEpoch + 1, 0));

		Assert.Multiple(async () =>
		{
			Assert.That(await StreamAssertions.ClosesAsync(local), Is.True);
			Assert.That(_carrier.Written.Any(frame => frame.Type == LinkFrameType.Close), Is.False);
			Assert.That(_link.IsLinked, Is.True);
		});
	}

	[Test]
	public async Task A_late_write_from_a_reset_stream_never_reaches_the_new_stream_with_the_same_id()
	{
		await HandshakeAsync();
		_carrier.Send(LinkFrame.Open(7));
		var old = await _dialer.AcceptedAsync();
		_carrier.Send(LinkFrame.Hello(false, AppEpoch + 1, 0), LinkFrame.Open(7));
		var current = await _dialer.AcceptedAsync();

		try
		{
			await old.WriteAsync("old"u8.ToArray());
		}
		catch (IOException)
		{
		}

		await current.WriteAsync("new"u8.ToArray());
		await Eventually.True(() => _carrier.DataOn(7).Length == 3, "the new stream delivered its data");
		await SyncAsync();

		Assert.That(Encoding.ASCII.GetString(_carrier.DataOn(7)), Is.EqualTo("new"));
	}

	[Test]
	public async Task Malformed_input_restarts_the_host_with_a_new_epoch_and_drops_its_streams()
	{
		var firstEpoch = await HandshakeAsync();
		_carrier.Send(LinkFrame.Open(3));
		var local = await _dialer.AcceptedAsync();
		var written = _carrier.Written.Count;

		_carrier.SendRaw([0x09]);
		var restarted = LinkHello.Parse(await _carrier.NextAsync(IsHello(ack: false), written));

		Assert.Multiple(async () =>
		{
			Assert.That(restarted.Epoch, Is.Not.EqualTo(firstEpoch));
			Assert.That(_link.IsLinked, Is.False);
			Assert.That(await StreamAssertions.ClosesAsync(local), Is.True);
		});
	}

	[Test]
	public async Task A_frame_stuck_for_two_seconds_restarts_the_host_and_hellos_buffered_behind_it_are_answered()
	{
		await HandshakeAsync();
		var written = _carrier.Written.Count;
		var reads = _link.ReadsHandled;
		_carrier.SendRaw(LinkFrame.Data(4, new byte[400]).Encode()[..20]);
		await Eventually.True(() => _link.ReadsHandled > reads, "the partial frame was read");

		_time.Advance(TimeSpan.FromSeconds(1));
		reads = _link.ReadsHandled;
		_carrier.Send(LinkFrame.Hello(false, AppEpoch, 0));
		await Eventually.True(() => _link.ReadsHandled > reads, "the resent hello was read");
		var answeredWhileStuck = _carrier.Written.Skip(written).Any(IsHello(ack: true));
		_time.Advance(TimeSpan.FromSeconds(1));
		var restarted = LinkHello.Parse(await _carrier.NextAsync(IsHello(ack: false), written));
		var answer = LinkHello.Parse(await _carrier.NextAsync(frame => IsHello(ack: true)(frame) &&
			LinkHello.Parse(frame).Epoch == restarted.Epoch, written));
		_carrier.Send(LinkFrame.Hello(true, AppEpoch, restarted.Epoch));

		await Eventually.True(() => _link.IsLinked, "the link came back");
		Assert.Multiple(() =>
		{
			Assert.That(answeredWhileStuck, Is.False);
			Assert.That(answer.Echo, Is.EqualTo(AppEpoch));
		});
	}

	[Test]
	public async Task Both_sides_losing_the_link_at_the_same_time_recover()
	{
		var firstEpoch = await HandshakeAsync();
		var written = _carrier.Written.Count;

		for (var second = 0; second < 15; second++)
		{
			_time.Advance(TimeSpan.FromSeconds(1));
		}

		var restarted = LinkHello.Parse(await _carrier.NextAsync(IsHello(ack: false), written));
		_carrier.Send(LinkFrame.Hello(false, AppEpoch + 7, 0), LinkFrame.Hello(true, AppEpoch + 7, restarted.Epoch));
		await _carrier.NextAsync(frame => IsHello(ack: true)(frame) && LinkHello.Parse(frame).Echo == AppEpoch + 7);

		await Eventually.True(() => _link.IsLinked, "both restarted epochs were answered");
		Assert.That(restarted.Epoch, Is.Not.EqualTo(firstEpoch));
	}

	[Test]
	public async Task An_idle_host_sends_a_keepalive_after_five_seconds_and_restarts_after_fifteen_silent_seconds()
	{
		var firstEpoch = await HandshakeAsync();
		var written = _carrier.Written.Count;

		for (var second = 0; second < 5; second++)
		{
			_time.Advance(TimeSpan.FromSeconds(1));
		}

		var keepalive = await _carrier.NextAsync(frame => frame.Type == LinkFrameType.Window, written);
		for (var second = 5; second < 15; second++)
		{
			_time.Advance(TimeSpan.FromSeconds(1));
		}

		var restarted = LinkHello.Parse(await _carrier.NextAsync(IsHello(ack: false), written));

		Assert.Multiple(() =>
		{
			Assert.That(keepalive.Stream, Is.EqualTo(LinkProtocol.ControlStream));
			Assert.That(keepalive.Credit, Is.Zero);
			Assert.That(restarted.Epoch, Is.Not.EqualTo(firstEpoch));
			Assert.That(_link.IsLinked, Is.False);
		});
	}

	[Test]
	public async Task Bye_closes_every_stream_and_the_link_is_no_longer_linked()
	{
		await HandshakeAsync();
		_carrier.Send(LinkFrame.Open(3));
		var local = await _dialer.AcceptedAsync();

		_carrier.Send(LinkFrame.Bye());

		Assert.That(await StreamAssertions.ClosesAsync(local), Is.True);
		await Eventually.True(() => _link.ByeReceived && !_link.IsLinked, "the link recorded the bye");
	}

	[Test]
	public async Task Once_linked_a_frame_that_cannot_be_written_within_fifteen_seconds_loses_the_link()
	{
		await HandshakeAsync();
		_carrier.WriteOverride = (_, token) => new ValueTask(Task.Delay(Timeout.Infinite, token));
		var attempts = _carrier.WriteAttempts;
		_carrier.Send(LinkFrame.Hello(false, AppEpoch, 0));
		await Eventually.True(() => _carrier.WriteAttempts > attempts, "the answer is being written");

		_time.Advance(TimeSpan.FromSeconds(14));
		var stillRunning = !_run.IsCompleted;
		_time.Advance(TimeSpan.FromSeconds(1));

		await _run.WaitAsync(TimeSpan.FromSeconds(10));
		Assert.That(stillRunning, Is.True);
	}

	[Test]
	public async Task Before_the_app_ever_answers_a_blocked_hello_keeps_the_link_and_hellos_do_not_pile_up()
	{
		await StopLink();
		_time = new FakeTimeProvider();
		_carrier = new FakeLinkCarrier
		{
			WriteOverride = (_, token) => new ValueTask(Task.Delay(Timeout.Infinite, token))
		};
		_dialer = new LoopbackTestDialer();
		_link = new NativeLink(_carrier, "usb:TEST", _dialer, _time, Logger.None);
		_stop = new CancellationTokenSource();
		_run = _link.RunAsync(_stop.Token);
		await Eventually.True(() => _carrier.WriteAttempts == 1, "the first hello is being written");

		for (var round = 1; round <= 3; round++)
		{
			for (var second = 0; second < 15; second++)
			{
				_time.Advance(TimeSpan.FromSeconds(1));
			}

			var expected = round + 1;
			await Eventually.True(() => _carrier.WriteAttempts >= expected, "the timed out hello was followed by another");
		}

		Assert.Multiple(() =>
		{
			Assert.That(_run.IsCompleted, Is.False);
			Assert.That(_carrier.WriteAttempts, Is.EqualTo(4));
		});
	}

	[Test]
	public async Task Stopping_does_not_wait_for_a_write_that_ignores_cancellation()
	{
		await HandshakeAsync();
		var never = new TaskCompletionSource();
		_carrier.WriteOverride = (_, _) => new ValueTask(never.Task);
		var attempts = _carrier.WriteAttempts;
		_carrier.Send(LinkFrame.Hello(false, AppEpoch, 0));
		await Eventually.True(() => _carrier.WriteAttempts > attempts, "the answer is being written");

		var stopwatch = Stopwatch.StartNew();
		await _stop.CancelAsync();
		await _run.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.That(stopwatch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(3)));
		never.SetResult();
	}

	private async Task<uint> HandshakeAsync()
	{
		var hello = LinkHello.Parse(await _carrier.NextAsync(IsHello(ack: false)));
		_carrier.Send(LinkFrame.Hello(true, AppEpoch, hello.Epoch), LinkFrame.Hello(false, AppEpoch, 0));
		await Eventually.True(() => _link.IsLinked, "the handshake completed");
		await _carrier.NextAsync(frame => IsHello(ack: true)(frame) && LinkHello.Parse(frame).Echo == AppEpoch);
		return hello.Epoch;
	}

	private async Task SyncAsync(LinkFrame? probe = null)
	{
		var written = _carrier.Written.Count;
		_carrier.Send(probe ?? LinkFrame.Hello(false, AppEpoch, 0));
		await _carrier.NextAsync(IsHello(ack: true), written);
	}

	private static Func<LinkFrame, bool> IsHello(bool ack)
		=> frame => frame.Type == LinkFrameType.Hello && ((frame.Flags & LinkProtocol.AckFlag) != 0) == ack;
}
