using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class PluginSessionConnectionTests
{
	private FakePluginSocket _socket = null!;
	private CapabilityDispatcher _dispatcher = null!;

	[SetUp]
	public void SetUp()
	{
		_socket = new FakePluginSocket();
		_dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions"));
	}

	[TearDown]
	public async Task TearDown()
	{
		_dispatcher.Dispose();
		await _socket.DisposeAsync();
	}

	private PluginSessionConnection Connection(PluginSession? session = null, TimeProvider? time = null)
		=> new(_socket,
			session ?? TestSession.Create(),
			_dispatcher,
			new PluginConnectionState(),
			time ?? TimeProvider.System,
			Serilog.Core.Logger.None);

	private void Welcome(bool resumed = false)
		=> _socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionWelcome,
			Id = "welcome",
			Payload = FakePluginSocket.Payload(new SessionWelcomePayload { SessionId = "session-1", Resumed = resumed })
		});

	[Test]
	public async Task The_handshake_asserts_the_negotiated_version_and_session()
	{
		var connection = Connection();
		Welcome();

		var run = connection.RunAsync(null, "instance-1", CancellationToken.None);
		var hello = await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.CloseFromHost(1000);
		await run;

		var payload = hello.Payload!.Value.Deserialize<SessionHelloPayload>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(payload.ProtocolVersion, Is.EqualTo(1));
			Assert.That(payload.SessionId, Is.EqualTo("session-1"));
			Assert.That(payload.InstanceId, Is.EqualTo("instance-1"));
			Assert.That(payload.ResumeSessionId, Is.Null);
		});
	}

	[Test]
	public async Task A_resume_attempt_names_the_session_it_is_resuming()
	{
		var connection = Connection();
		Welcome(resumed: true);

		var run = connection.RunAsync("session-1", "instance-1", CancellationToken.None);
		var hello = await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.CloseFromHost(1000);
		await run;

		var payload = hello.Payload!.Value.Deserialize<SessionHelloPayload>(PluginProtocolJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(payload.ResumeSessionId, Is.EqualTo("session-1"));
			Assert.That(connection.Resumed, Is.True);
		});
	}

	[TestCase(ProtocolCloseCodes.ProtocolVersionUnsupported)]
	[TestCase(ProtocolCloseCodes.SessionReplaced)]
	[TestCase(ProtocolCloseCodes.AuthenticationFailed)]
	[TestCase(ProtocolCloseCodes.SupervisorShutdown)]
	[TestCase(ProtocolCloseCodes.RegistrationRejected)]
	public async Task A_protocol_close_code_ends_the_connection_for_good(int closeCode)
	{
		var connection = Connection();
		Welcome();

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.CloseFromHost(closeCode);
		var outcome = await run;

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Fatal, Is.True);
			Assert.That(outcome.CloseCode, Is.EqualTo(closeCode));
		});
	}

	[TestCase(1006)]
	[TestCase(ProtocolCloseCodes.SessionExpired)]
	[TestCase(ProtocolCloseCodes.QueueOverflow)]
	public async Task Any_other_close_is_worth_retrying(int closeCode)
	{
		var connection = Connection();
		Welcome();

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.CloseFromHost(closeCode);

		Assert.That((await run).Fatal, Is.False);
	}

	[Test]
	public async Task An_inbound_ping_is_answered_with_a_correlated_pong()
	{
		var connection = Connection();
		Welcome();

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.Push(new ProtocolEnvelope { Type = MessageTypes.SessionPing, Id = "ping-1" });
		var pong = await _socket.NextAsync(MessageTypes.SessionPong);

		_socket.CloseFromHost(1000);
		await run;

		Assert.That(pong.CorrelationId, Is.EqualTo("ping-1"));
	}

	[Test]
	public async Task An_unknown_message_type_is_reported_and_the_socket_stays_open()
	{
		var connection = Connection();
		Welcome();

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.PushRaw("""{"type":"from.the.future","id":"future-1"}""");
		var error = await _socket.NextAsync(MessageTypes.ProtocolError);

		// Tolerance is the contract: a newer host must be able to say something this plugin has never
		// heard of without the session dying over it.
		_socket.Push(new ProtocolEnvelope { Type = MessageTypes.SessionPing, Id = "ping-1" });
		var pong = await _socket.NextAsync(MessageTypes.SessionPong);

		_socket.CloseFromHost(1000);
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(error.Error!.Code, Is.EqualTo(ProtocolErrorCodes.UnknownMessageType));
			Assert.That(error.CorrelationId, Is.EqualTo("future-1"), "the parsed id makes the complaint attributable");
			Assert.That(pong, Is.Not.Null);
		});
	}

	[Test]
	public async Task A_malformed_frame_is_reported_and_the_socket_stays_open()
	{
		var connection = Connection();
		Welcome();

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.PushRaw("{ not json at all");
		var error = await _socket.NextAsync(MessageTypes.ProtocolError);

		_socket.CloseFromHost(1000);
		await run;

		Assert.That(error.Error!.Code, Is.EqualTo(ProtocolErrorCodes.MalformedEnvelope));
	}

	[Test]
	public async Task An_invocation_is_dispatched_and_answered()
	{
		var connection = Connection();
		Welcome();

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityInvoke,
			Id = "invoke-1",
			Payload = FakePluginSocket.Payload(new CapabilityInvokePayload
			{
				Kind = "actions",
				LocalId = "one",
				Operation = "execute"
			})
		});

		var result = await _socket.NextAsync(MessageTypes.CapabilityResult);

		_socket.CloseFromHost(1000);
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(result.CorrelationId, Is.EqualTo("invoke-1"));
			Assert.That(result.Error, Is.Null);
		});
	}

	[Test]
	public async Task Filling_the_inbound_queue_asks_the_host_to_pause()
	{
		// A tiny queue makes the watermark reachable; the ratio is what is under test, not the size.
		var session = TestSession.Create(maxInboundQueueDepth: ProtocolLimits.QueueHighWatermark + 4);

		var blocked = new TaskCompletionSource();
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions",
			async (_, _) =>
			{
				await blocked.Task;
				return CapabilityInvocationResult.Ok();
			}));

		var connection = new PluginSessionConnection(_socket,
			session,
			dispatcher,
			new PluginConnectionState(),
			TimeProvider.System,
			Serilog.Core.Logger.None);
		Welcome();

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		for (var index = 0; index < ProtocolLimits.QueueHighWatermark + 2; index++)
		{
			_socket.Push(new ProtocolEnvelope
			{
				Type = MessageTypes.CapabilityInvoke,
				Id = $"invoke-{index}",
				Payload = FakePluginSocket.Payload(new CapabilityInvokePayload
				{
					Kind = "actions",
					LocalId = "one",
					Operation = "execute"
				})
			});
		}

		var pause = await _socket.NextAsync(MessageTypes.FlowPause);

		blocked.TrySetResult();
		_socket.CloseFromHost(1000);
		await run;

		Assert.That(pause.Payload, Is.Not.Null);
	}

	[Test]
	public async Task Two_invocations_are_genuinely_in_flight_at_the_same_time()
	{
		var entered = 0;
		var bothEntered = new TaskCompletionSource();
		var release = new TaskCompletionSource();

		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions",
			async (_, _) =>
			{
				if (Interlocked.Increment(ref entered) == 2)
				{
					bothEntered.TrySetResult();
				}

				await release.Task;
				return CapabilityInvocationResult.Ok();
			}));

		var connection = new PluginSessionConnection(_socket,
			TestSession.Create(),
			dispatcher,
			new PluginConnectionState(),
			TimeProvider.System,
			Serilog.Core.Logger.None);
		Welcome();

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.Push(Invoke("invoke-a"));
		_socket.Push(Invoke("invoke-b"));

		// A sequential process loop would never start the second invocation until the first returns -
		// this is what deadlocks a handler that awaits a callback from the host, since that callback's
		// reply is a message the loop would never get around to reading. Timing out here is the failure.
		await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

		release.TrySetResult();
		await _socket.NextAsync(MessageTypes.CapabilityResult);
		await _socket.NextAsync(MessageTypes.CapabilityResult);

		_socket.CloseFromHost(1000);
		await run;

		Assert.That(entered, Is.EqualTo(2));
	}

	[Test]
	public async Task Concurrency_is_capped_at_the_protocol_limit()
	{
		var current = 0;
		var peak = 0;
		var release = new TaskCompletionSource();

		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions",
			async (_, _) =>
			{
				var running = Interlocked.Increment(ref current);
				InterlockedMax(ref peak, running);

				await release.Task;

				Interlocked.Decrement(ref current);
				return CapabilityInvocationResult.Ok();
			}));

		var connection = new PluginSessionConnection(_socket,
			TestSession.Create(),
			dispatcher,
			new PluginConnectionState(),
			TimeProvider.System,
			Serilog.Core.Logger.None);
		Welcome();

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		const int total = ProtocolLimits.MaxConcurrentInvocations + 5;

		for (var index = 0; index < total; index++)
		{
			_socket.Push(Invoke($"invoke-{index}"));
		}

		await Wait.UntilAsync(() => Volatile.Read(ref current) == ProtocolLimits.MaxConcurrentInvocations);

		// The gate has been at the limit for a beat; if the loop were going to overshoot it, it would
		// have by now rather than leaving the rest queued behind the concurrency semaphore.
		await Task.Delay(TimeSpan.FromMilliseconds(100));
		Assert.That(Volatile.Read(ref current), Is.EqualTo(ProtocolLimits.MaxConcurrentInvocations));

		release.TrySetResult();

		for (var index = 0; index < total; index++)
		{
			var result = await _socket.NextAsync(MessageTypes.CapabilityResult);

			// A real cap queues the rest until a slot frees; only a fire-and-forget loop would answer
			// them immediately with a rate-limit failure instead.
			Assert.That(result.Error, Is.Null);
		}

		_socket.CloseFromHost(1000);
		await run;

		Assert.That(peak, Is.EqualTo(ProtocolLimits.MaxConcurrentInvocations));
	}

	[Test]
	public async Task A_graceful_close_awaits_an_in_flight_dispatch()
	{
		var started = new TaskCompletionSource();
		var release = new TaskCompletionSource();
		var completed = false;

		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions",
			async (_, _) =>
			{
				started.TrySetResult();

				// Deliberately does not observe the invocation token: a handler that keeps working past
				// the point the connection starts tearing down is the case that proves teardown waited.
				await release.Task;
				completed = true;
				return CapabilityInvocationResult.Ok();
			}));

		var connection = new PluginSessionConnection(_socket,
			TestSession.Create(),
			dispatcher,
			new PluginConnectionState(),
			TimeProvider.System,
			Serilog.Core.Logger.None);
		Welcome();

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.Push(Invoke("invoke-1"));
		await started.Task.WaitAsync(TimeSpan.FromSeconds(5));

		_socket.CloseFromHost(1000);

		var finishedTooEarly = await Task.WhenAny(run, Task.Delay(TimeSpan.FromMilliseconds(200))) == run;
		Assert.That(finishedTooEarly, Is.False, "the connection tore down before the in-flight dispatch finished");

		release.TrySetResult();
		await run;

		Assert.That(completed, Is.True);
	}

	[Test]
	public async Task A_throwing_handler_is_reported_and_the_process_loop_keeps_working()
	{
		var calls = 0;

		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions",
			(_, _) => Interlocked.Increment(ref calls) == 1
				? throw new InvalidOperationException("boom")
				: Task.FromResult(CapabilityInvocationResult.Ok())));

		var connection = new PluginSessionConnection(_socket,
			TestSession.Create(),
			dispatcher,
			new PluginConnectionState(),
			TimeProvider.System,
			Serilog.Core.Logger.None);
		Welcome();

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.Push(Invoke("invoke-1"));
		var failed = await _socket.NextAsync(MessageTypes.CapabilityResult);

		// The loop must still be alive after that: a second invocation is answered rather than the
		// connection hanging or the process loop having quietly faulted.
		_socket.Push(Invoke("invoke-2"));
		var succeeded = await _socket.NextAsync(MessageTypes.CapabilityResult);

		_socket.CloseFromHost(1000);
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(failed.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InternalError));
			Assert.That(succeeded.Error, Is.Null);
		});
	}

	private static ProtocolEnvelope Invoke(string id) => new()
	{
		Type = MessageTypes.CapabilityInvoke,
		Id = id,
		Payload = FakePluginSocket.Payload(new CapabilityInvokePayload
		{
			Kind = "actions",
			LocalId = "one",
			Operation = "execute"
		})
	};

	private static void InterlockedMax(ref int target, int candidate)
	{
		int initial;

		do
		{
			initial = Volatile.Read(ref target);

			if (candidate <= initial)
			{
				return;
			}
		} while (Interlocked.CompareExchange(ref target, candidate, initial) != initial);
	}

	[Test]
	public async Task Saying_goodbye_closes_the_socket_normally()
	{
		var connection = Connection();

		await connection.GoodbyeAsync("shutting down", CancellationToken.None);
		var goodbye = await _socket.NextAsync(MessageTypes.SessionGoodbye);

		Assert.Multiple(() =>
		{
			Assert.That(goodbye.Payload, Is.Not.Null);
			Assert.That(_socket.CloseCode, Is.EqualTo(1000));
		});
	}

	[Test]
	public async Task GoodbyeAsync_Does_Not_Park_Forever_When_The_Host_Never_Drains_The_Socket()
	{
		// Regression for issue #413 finding 1's SDK-side mirror: a host that stops reading its socket
		// used to park SendDirectAsync (holding _sending) forever, since it had no timeout of its own.
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		_socket.SendGate = new TaskCompletionSource<bool>();
		var connection = Connection(time: time);

		var goodbye = connection.GoodbyeAsync("shutting down", CancellationToken.None);

		time.Advance(ProtocolTimeouts.DefaultRequest);

		// GoodbyeAsync itself swallows the wedge (politeness towards a host that cannot be reached) -
		// what matters is that it returns instead of hanging, and that the socket was aborted rather
		// than left in limbo.
		await goodbye;

		Assert.That(_socket.Aborted, Is.True);

		// Release the parked send so it doesn't linger past the test.
		_socket.SendGate.TrySetResult(true);
	}

	[Test]
	public async Task A_host_that_never_welcomes_is_retried_rather_than_treated_as_fatal()
	{
		var session = TestSession.Create() with { Timeouts = TestSession.Timeouts() };
		var connection = Connection(session);

		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.CloseFromHost(1006);

		Assert.That((await run).Fatal, Is.False);
	}
}
