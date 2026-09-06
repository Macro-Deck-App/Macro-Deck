using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Testing;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// <see cref="HostInvoker" /> against the SDK's own fakes, mirroring the shape of the host's
/// <c>PluginCapabilityInvokerTests</c> for the opposite direction of the same exchange.
/// </summary>
[TestFixture]
public class HostInvokerTests
{
	/// <summary>Wires a connected <see cref="HostInvoker" /> the way <c>DeadlockRegressionTests</c> does:
	/// <see cref="PluginConnectionState.ActiveConnection" /> is normally set by
	/// <c>PluginConnectionHostedService</c> once the handshake completes; set directly here since these
	/// tests drive the connection without that service.</summary>
	private static (HostInvoker HostInvoker, FakePluginSocket Socket, Task<ConnectionOutcome> Run) Connect(
		TimeProvider timeProvider)
	{
		var state = new PluginConnectionState();
		var hostInvoker = new HostInvoker(state, timeProvider, Serilog.Core.Logger.None);

		var socket = new FakePluginSocket();
		var connection = new PluginSessionConnection(socket,
			TestSession.Create(),
			TestSession.Dispatcher(),
			state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			hostInvoker,
			hostStateCache: null);

		state.ActiveConnection = connection;

		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionWelcome,
			Id = "welcome",
			Payload = FakePluginSocket.Payload(new SessionWelcomePayload { SessionId = "session-1", Resumed = false })
		});

		var run = connection.RunAsync(null, "instance-1", CancellationToken.None);
		return (hostInvoker, socket, run);
	}

	private static async Task<ProtocolEnvelope> AwaitHandshakeAsync(FakePluginSocket socket)
		=> await socket.NextAsync(MessageTypes.SessionHello);

	[Test]
	public async Task A_successful_round_trip_returns_the_result_payload()
	{
		var (hostInvoker, socket, run) = Connect(TimeProvider.System);
		await AwaitHandshakeAsync(socket);

		var invokeTask = hostInvoker.InvokeAsync(HostApis.Variables,
			HostOperations.Variables.List,
			null,
			CancellationToken.None);

		var hostInvoke = await socket.NextAsync(MessageTypes.HostInvoke);
		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.HostResult,
			Id = "result-1",
			CorrelationId = hostInvoke.Id,
			Payload = FakePluginSocket.Payload(new HostResultPayload { Data = FakePluginSocket.Payload("ok") })
		});

		var result = await invokeTask;

		Assert.That(result!.Value.GetString(), Is.EqualTo("ok"));

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task An_error_result_maps_to_HostInvocationException_with_the_wire_code_preserved()
	{
		var (hostInvoker, socket, run) = Connect(TimeProvider.System);
		await AwaitHandshakeAsync(socket);

		var invokeTask = hostInvoker.InvokeAsync(HostApis.Notifications,
			HostOperations.Notifications.Notify,
			null,
			CancellationToken.None);

		var hostInvoke = await socket.NextAsync(MessageTypes.HostInvoke);
		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.HostResult,
			Id = "result-1",
			CorrelationId = hostInvoke.Id,
			Error = new ProtocolError { Code = "SOME_WIRE_CODE", Message = "boom", Retryable = false }
		});

		var exception = Assert.CatchAsync<HostInvocationException>(async () => await invokeTask);

		Assert.That(exception!.Code, Is.EqualTo("SOME_WIRE_CODE"));

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task The_timeout_budget_expiring_yields_a_retryable_timeout_and_sends_host_cancel()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		var (hostInvoker, socket, run) = Connect(time);
		await AwaitHandshakeAsync(socket);

		var invokeTask = hostInvoker.InvokeAsync(HostApis.Variables,
			HostOperations.Variables.List,
			null,
			CancellationToken.None);

		var hostInvoke = await socket.NextAsync(MessageTypes.HostInvoke);
		time.Advance(ProtocolTimeouts.DefaultRequest);

		var exception = Assert.CatchAsync<HostInvocationException>(async () => await invokeTask);

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.Timeout));
			Assert.That(exception.Retryable, Is.True);
		});

		var cancel = await socket.NextAsync(MessageTypes.HostCancel);
		Assert.That(cancel.CorrelationId, Is.EqualTo(hostInvoke.Id));

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task Caller_cancellation_puts_host_cancel_on_the_wire_and_returns_promptly()
	{
		var (hostInvoker, socket, run) = Connect(TimeProvider.System);
		await AwaitHandshakeAsync(socket);

		using var cts = new CancellationTokenSource();
		var invokeTask = hostInvoker.InvokeAsync(HostApis.Variables, HostOperations.Variables.List, null, cts.Token);

		var hostInvoke = await socket.NextAsync(MessageTypes.HostInvoke);
		await cts.CancelAsync();

		Assert.CatchAsync<OperationCanceledException>(async () => await invokeTask);

		var cancel = await socket.NextAsync(MessageTypes.HostCancel);
		Assert.That(cancel.CorrelationId, Is.EqualTo(hostInvoke.Id));

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public async Task An_invocation_still_in_flight_when_the_connection_ends_fails_retryably()
	{
		// A manual clock that is never advanced, so the request timeout cannot fire: the invocation has
		// to be failed by the connection ending, or not at all.
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		var (hostInvoker, socket, run) = Connect(time);
		await AwaitHandshakeAsync(socket);

		var invokeTask = hostInvoker.InvokeAsync(HostApis.Config,
			HostOperations.Config.Entries,
			null,
			CancellationToken.None);

		await socket.NextAsync(MessageTypes.HostInvoke);

		// The host goes away without answering.
		socket.CloseFromHost(1006);
		await run;

		// Bounded, so a regression fails the test rather than hanging it.
		var exception = Assert.CatchAsync<HostInvocationException>(async ()
			=> await invokeTask.WaitAsync(TimeSpan.FromSeconds(5)));

		Assert.Multiple(() =>
		{
			// Retryable, because a reconnect is exactly what makes this call viable again - the same
			// answer the invoker already gives when there is no connection at all.
			Assert.That(exception!.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(exception.Retryable, Is.True);
		});
	}

	[Test]
	public async Task A_result_the_host_sends_before_closing_is_still_returned()
	{
		var time = new ManualTimeProvider(DateTimeOffset.UnixEpoch);
		var (hostInvoker, socket, run) = Connect(time);
		await AwaitHandshakeAsync(socket);

		var invokeTask = hostInvoker.InvokeAsync(HostApis.Variables,
			HostOperations.Variables.List,
			null,
			CancellationToken.None);

		var hostInvoke = await socket.NextAsync(MessageTypes.HostInvoke);

		// Answered and closed in one breath - what a host shutting down after finishing what it owed
		// looks like. The answer arrived before the close, so the close must not lose it.
		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.HostResult,
			Id = "result-1",
			CorrelationId = hostInvoke.Id,
			Payload = FakePluginSocket.Payload(new HostResultPayload { Data = FakePluginSocket.Payload("ok") })
		});
		socket.CloseFromHost(1000);

		var result = await invokeTask.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(result!.Value.GetString(), Is.EqualTo("ok"));

		await run;
	}

	[Test]
	public async Task A_host_result_for_an_unknown_correlation_is_reported_and_does_not_fault_the_connection()
	{
		var (hostInvoker, socket, run) = Connect(TimeProvider.System);
		await AwaitHandshakeAsync(socket);

		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.HostResult,
			Id = "stray-result",
			CorrelationId = "never-issued",
			Payload = FakePluginSocket.Payload(new HostResultPayload())
		});

		var error = await socket.NextAsync(MessageTypes.ProtocolError);
		Assert.That(error.Error!.Code, Is.EqualTo(ProtocolErrorCodes.CorrelationUnknown));

		// The connection survives an unrecognised correlation: a fresh, unrelated round trip still works.
		var invokeTask = hostInvoker.InvokeAsync(HostApis.Variables,
			HostOperations.Variables.List,
			null,
			CancellationToken.None);
		var hostInvoke = await socket.NextAsync(MessageTypes.HostInvoke);
		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.HostResult,
			Id = "result-2",
			CorrelationId = hostInvoke.Id,
			Payload = FakePluginSocket.Payload(new HostResultPayload())
		});

		Assert.DoesNotThrowAsync(async () => await invokeTask);

		socket.CloseFromHost(1000);
		await run;
	}

	[Test]
	public void A_late_result_naming_a_correlation_this_invoker_no_longer_knows_is_dropped_silently()
	{
		var state = new PluginConnectionState();
		var hostInvoker = new HostInvoker(state, TimeProvider.System, Serilog.Core.Logger.None);

		// Nothing was ever invoked, so this correlation was never pending - the same shape a reply
		// crossing on the wire after this invoker already gave up on it would have. TryComplete must
		// report it as unrecognised rather than throw or resurrect anything.
		var accepted = hostInvoker.TryComplete(new ProtocolEnvelope
		{
			Type = MessageTypes.HostResult,
			Id = "late",
			CorrelationId = "abandoned-correlation",
			Payload = FakePluginSocket.Payload(new HostResultPayload())
		});

		Assert.That(accepted, Is.False);
	}
}
