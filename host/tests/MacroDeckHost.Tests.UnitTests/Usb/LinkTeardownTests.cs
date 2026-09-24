using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using MacroDeckHost.Infrastructure.Usb.Native;
using MacroDeckHost.Tests.UnitTests.Delegation;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Usb;

[NonParallelizable]
public class LinkTeardownTests
{
	private const uint AppEpoch = 0xB0B0B0B0;

	[Test]
	public async Task A_link_whose_carrier_fails_to_read_leaves_no_unobserved_exception()
	{
		var unobserved = await CollectUnobservedAsync(async () =>
		{
			var carrier = new FakeLinkCarrier();
			var session = StartSession(carrier);
			await LinkAsync(carrier, session);
			carrier.Send(LinkFrame.Open(3));
			carrier.Lose();
			await session.Completion.WaitAsync(TimeSpan.FromSeconds(10));
			await session.LoopsEnded.WaitAsync(TimeSpan.FromSeconds(10));
			session.Stop();
		});

		Assert.That(unobserved, Is.Empty);
	}

	[Test]
	public async Task A_link_whose_carrier_fails_to_write_leaves_no_unobserved_exception()
	{
		var unobserved = await CollectUnobservedAsync(async () =>
		{
			var carrier = new FakeLinkCarrier();
			var session = StartSession(carrier);
			await LinkAsync(carrier, session);
			carrier.WriteOverride = (_, _) => ValueTask.FromException(new LinkLostException("The test write failed."));
			carrier.Send(LinkFrame.Hello(false, AppEpoch, 0));
			await session.Completion.WaitAsync(TimeSpan.FromSeconds(10));
			await session.LoopsEnded.WaitAsync(TimeSpan.FromSeconds(10));
		});

		Assert.That(unobserved, Is.Empty);
	}

	[Test]
	public async Task A_write_that_fails_after_the_stop_grace_leaves_no_unobserved_exception()
	{
		var unobserved = await CollectUnobservedAsync(async () =>
		{
			var carrier = new FakeLinkCarrier();
			var session = StartSession(carrier);
			await LinkAsync(carrier, session);
			var blocked = new TaskCompletionSource();
			var attempts = carrier.WriteAttempts;
			carrier.WriteOverride = (_, _) => new ValueTask(blocked.Task);
			carrier.Send(LinkFrame.Hello(false, AppEpoch, 0));
			await Eventually.True(() => carrier.WriteAttempts > attempts, "a write is blocked");

			session.Stop();
			await session.Completion.WaitAsync(TimeSpan.FromSeconds(10));
			blocked.SetException(new LinkLostException("The test write failed late."));
			await session.LoopsEnded.WaitAsync(TimeSpan.FromSeconds(10));
			session.Stop();
		});

		Assert.That(unobserved, Is.Empty);
	}

	[Test]
	public async Task An_accessory_carrier_whose_transfers_fail_while_closing_leaves_no_unobserved_exception()
	{
		var unobserved = await CollectUnobservedAsync(async () =>
		{
			var carrier = new AccessoryLinkCarrier(new FailingPipe(), new FakeTimeProvider());
			var read = carrier.ReadAsync(new byte[LinkProtocol.MaxFrameLength], CancellationToken.None).AsTask();
			var write = carrier.WriteFrameAsync(new byte[10], CancellationToken.None).AsTask();
			await carrier.DisposeAsync();
			_ = read.Exception;
			_ = write.Exception;
		});

		Assert.That(unobserved, Is.Empty);
	}

	[Test]
	public async Task An_unexpected_fault_is_logged_as_an_error_and_teardown_faults_are_not()
	{
		var sink = new CollectingSink();
		var logger = new Serilog.LoggerConfiguration().WriteTo.Sink(sink).CreateLogger();

		await TaskObservation.Settle(Task.FromException(new InvalidOperationException("a bug")), logger);
		await TaskObservation.Settle(Task.FromException(new LinkLostException("gone")), logger);
		await TaskObservation.Settle(Task.FromException(new ObjectDisposedException("carrier")), logger);
		await TaskObservation.Settle(Task.FromCanceled(new CancellationToken(true)), logger);

		Assert.Multiple(() =>
		{
			Assert.That(sink.Events, Has.Count.EqualTo(1));
			Assert.That(sink.Events[0].Level, Is.EqualTo(Serilog.Events.LogEventLevel.Error));
			Assert.That(sink.Events[0].Exception, Is.TypeOf<InvalidOperationException>());
		});
	}

	private sealed class CollectingSink : Serilog.Core.ILogEventSink
	{
		public List<Serilog.Events.LogEvent> Events { get; } = [];

		public void Emit(Serilog.Events.LogEvent logEvent) => Events.Add(logEvent);
	}

	private static NativeLinkSession StartSession(FakeLinkCarrier carrier)
		=> new(carrier, "usb:TEARDOWN", new LoopbackTestDialer(), TimeProvider.System, Logger.None);

	private static async Task LinkAsync(FakeLinkCarrier carrier, NativeLinkSession session)
	{
		var hello = LinkHello.Parse(await carrier.NextAsync(frame => frame.Type == LinkFrameType.Hello));
		carrier.Send(LinkFrame.Hello(true, AppEpoch, hello.Epoch), LinkFrame.Hello(false, AppEpoch, 0));
		await Eventually.True(() => session.IsLinked, "the link came up");
	}

	private static async Task<IReadOnlyList<Exception>> CollectUnobservedAsync(Func<Task> scenario)
	{
		var unobserved = new ConcurrentQueue<Exception>();
		void Record(object? sender, UnobservedTaskExceptionEventArgs args)
		{
			if (args.Exception.Flatten().InnerExceptions.Any(IsLinkTeardown))
			{
				unobserved.Enqueue(args.Exception);
			}
		}

		TaskScheduler.UnobservedTaskException += Record;
		try
		{
			await RunDetachedAsync(scenario);
			for (var pass = 0; pass < 3; pass++)
			{
				GC.Collect();
				GC.WaitForPendingFinalizers();
			}
		}
		finally
		{
			TaskScheduler.UnobservedTaskException -= Record;
		}

		return unobserved.ToList();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static Task RunDetachedAsync(Func<Task> scenario) => scenario();

	private static bool IsLinkTeardown(Exception exception)
		=> exception is LinkLostException or ObjectDisposedException or TimeoutException ||
			exception.StackTrace?.Contains("MacroDeckHost.Infrastructure.Usb.Native", StringComparison.Ordinal) == true;

	private sealed class FailingPipe : IUsbBulkPipe
	{
		public UsbTransferResult Read(byte[] buffer, int timeoutMilliseconds)
		{
			Thread.Sleep(50);
			return new UsbTransferResult(UsbTransferStatus.Failed, 0);
		}

		public UsbTransferResult Write(byte[] buffer, int offset, int count, int timeoutMilliseconds)
		{
			Thread.Sleep(50);
			return new UsbTransferResult(UsbTransferStatus.Failed, 0);
		}

		public void Dispose()
		{
		}
	}
}
