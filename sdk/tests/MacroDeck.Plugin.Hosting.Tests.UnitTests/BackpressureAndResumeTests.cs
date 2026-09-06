using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// The queue, flow-control and resume behaviour: the parts of the client that only misbehave when the
/// host is slow, fast or restarting, and that nothing else exercises.
/// </summary>
[TestFixture]
public class BackpressureAndResumeTests
{
	private FakePluginSocket _socket = null!;

	[SetUp]
	public void SetUp() => _socket = new FakePluginSocket();

	[TearDown]
	public async Task TearDown() => await _socket.DisposeAsync();

	private static ProtocolEnvelope Invoke(int index) => new()
	{
		Type = MessageTypes.CapabilityInvoke,
		Id = $"invoke-{index}",
		Payload = FakePluginSocket.Payload(new CapabilityInvokePayload
		{
			Kind = "actions",
			LocalId = "one",
			Operation = "execute"
		})
	};

	private void Welcome()
		=> _socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionWelcome,
			Id = "welcome",
			Payload = FakePluginSocket.Payload(new Protocol.Handshake.SessionWelcomePayload
			{
				SessionId = "session-1",
				Resumed = false
			})
		});

	[Test]
	public async Task Overflowing_the_inbound_queue_reports_it_and_closes_with_1013()
	{
		// The handler never returns, so nothing drains: the queue is the only thing that can give.
		var blocked = new TaskCompletionSource();
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions",
			async (_, _) =>
			{
				await blocked.Task;
				return CapabilityInvocationResult.Ok();
			}));

		var session = TestSession.Create(maxInboundQueueDepth: 4);
		var connection = new PluginSessionConnection(_socket,
			session,
			dispatcher,
			new PluginConnectionState(),
			TimeProvider.System,
			Serilog.Core.Logger.None);

		Welcome();
		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		for (var index = 0; index < 40; index++)
		{
			_socket.Push(Invoke(index));
		}

		var error = await _socket.NextAsync(MessageTypes.ProtocolError);

		// Released before the connection is awaited, not after: a graceful close waits for every
		// dispatch it has handed out, and this handler ignores the invocation token, so a still-blocked
		// handler would leave RunAsync waiting on it forever.
		blocked.TrySetResult();

		var outcome = await run;

		Assert.Multiple(() =>
		{
			Assert.That(error.Error!.Code, Is.EqualTo(ProtocolErrorCodes.QueueOverflow));
			Assert.That(_socket.CloseCode, Is.EqualTo(ProtocolCloseCodes.QueueOverflow));

			// Overflow is a retry, not a fault: the host sent too much, which says nothing about
			// whether reconnecting would work.
			Assert.That(outcome.Fatal, Is.False);
		});
	}

	[Test]
	public async Task While_the_host_has_paused_us_only_exempt_messages_go_out()
	{
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions"));
		var connection = new PluginSessionConnection(_socket,
			TestSession.Create(),
			dispatcher,
			new PluginConnectionState(),
			TimeProvider.System,
			Serilog.Core.Logger.None);

		Welcome();
		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.FlowPause,
			Id = "pause-1",
			Payload = FakePluginSocket.Payload(new BackpressurePayload { Reason = "busy" })
		});

		// A ping queued behind the pause is the barrier. Both are handled by the one receive loop in
		// arrival order, so the pong coming back proves the pause is already in effect - otherwise this
		// would assert whichever of the two loops happened to win, and pass or fail with the load.
		_socket.Push(new ProtocolEnvelope { Type = MessageTypes.SessionPing, Id = "ping-1" });
		await _socket.NextAsync(MessageTypes.SessionPong);

		// event.publish is not exempt, so it waits; capability.result is, because it is what the host
		// is waiting on and holding it back would deadlock both sides.
		await connection.SendAsync(new ProtocolEnvelope { Type = MessageTypes.EventPublish, Id = "event-1" },
			CancellationToken.None);
		await connection.SendAsync(new ProtocolEnvelope
				{ Type = MessageTypes.CapabilityResult, Id = "result-1", CorrelationId = "x" },
			CancellationToken.None);

		// Whatever comes next, not the next result: filtering for the result would pass just as happily
		// if the held-back event had gone out ahead of it.
		var result = await _socket.NextAsync();

		// The held message follows once the host lifts the pause, rather than being dropped.
		_socket.Push(new ProtocolEnvelope { Type = MessageTypes.FlowResume, Id = "resume-1" });
		var published = await _socket.NextAsync();

		_socket.CloseFromHost(1000);
		await run;

		Assert.Multiple(() =>
		{
			Assert.That(result.Id, Is.EqualTo("result-1"));
			Assert.That(published.Id, Is.EqualTo("event-1"));
		});
	}

	[Test]
	public async Task A_goodbye_from_the_host_makes_the_session_unresumable()
	{
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions"));
		var connection = new PluginSessionConnection(_socket,
			TestSession.Create(),
			dispatcher,
			new PluginConnectionState(),
			TimeProvider.System,
			Serilog.Core.Logger.None);

		Welcome();
		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		_socket.Push(new ProtocolEnvelope { Type = MessageTypes.SessionGoodbye, Id = "goodbye-1" });

		// Wait for the message to be processed before ending the connection.
		await Wait.UntilAsync(() => connection.HostSaidGoodbye);

		_socket.CloseFromHost(1000);
		await run;

		// A session the host has said goodbye to is not resumable, so the next attempt must open a new
		// one rather than present an id the host has already forgotten.
		Assert.That(connection.HostSaidGoodbye, Is.True);
	}

	[Test]
	public async Task A_refused_resume_is_retried_without_reusing_the_dead_session()
	{
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions"));
		var connection = new PluginSessionConnection(_socket,
			TestSession.Create(),
			dispatcher,
			new PluginConnectionState(),
			TimeProvider.System,
			Serilog.Core.Logger.None);

		_socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.ProtocolError,
			Id = "error-1",
			Error = new ProtocolError
			{
				Code = ProtocolErrorCodes.SessionNotResumable,
				Message = ProtocolErrorMessages.For(ProtocolErrorCodes.SessionNotResumable),
				Retryable = false
			}
		});

		var outcome = await connection.RunAsync("session-1", null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.Fatal, Is.False);
			Assert.That(connection.HostSaidGoodbye, Is.True, "so the next attempt opens a new session");
		});
	}

	[TestCase(0, ExpectedResult = true)]
	[TestCase(59, ExpectedResult = true)]
	[TestCase(61, ExpectedResult = false)]
	[TestCase(3600, ExpectedResult = false)]
	public bool A_session_can_only_be_resumed_inside_the_resume_window(int secondsSinceDrop)
	{
		// The window is a minute and the session token lasts fifteen, so the window is always the
		// binding constraint - which is why there is no separate token refresh path.
		var session = TestSession.Create();
		var droppedAt = DateTimeOffset.UnixEpoch;

		return session.CanResumeAt(droppedAt.AddSeconds(secondsSinceDrop), droppedAt);
	}

	[Test]
	public async Task Silence_past_the_keepalive_timeout_drops_the_connection()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		using var dispatcher = TestSession.Dispatcher(new TestCapabilityHandler("actions"));

		var connection = new PluginSessionConnection(_socket,
			TestSession.Create(),
			dispatcher,
			new PluginConnectionState(),
			time,
			Serilog.Core.Logger.None);

		Welcome();
		var run = connection.RunAsync(null, null, CancellationToken.None);
		await _socket.NextAsync(MessageTypes.SessionHello);

		// Past the keep-alive timeout without a single inbound frame. The connection is dropped rather
		// than closed politely: there is nobody left to negotiate a close with.
		time.Advance(ProtocolTimeouts.KeepAliveTimeout + ProtocolTimeouts.KeepAliveInterval);

		await run;

		Assert.That(_socket.Aborted, Is.True);
	}
}
