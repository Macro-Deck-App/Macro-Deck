using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// Proves the exact deadlock issue #413 step 2 (concurrent capability dispatch) and step 6 (routing
/// <c>host.result</c> off the receive loop) exist to prevent: a capability handler that calls back
/// into the host - via <see cref="IHostInvoker" /> - while the host is still awaiting that very
/// invocation's <c>capability.result</c>. If <c>ProcessLoopAsync</c> ever went back to dispatching one
/// <c>capability.invoke</c> at a time, or <c>host.result</c> ever went back onto the queued dispatch
/// path instead of the receive loop, this handler's own <c>host.invoke</c> reply would be stuck behind
/// itself and the test would hang until its bounded timeout fails it.
/// </summary>
[TestFixture]
public class DeadlockRegressionTests
{
	[Test]
	public async Task A_handler_that_calls_back_into_the_host_while_the_host_awaits_its_result_completes()
	{
		var state = new PluginConnectionState();
		var hostInvoker = new HostInvoker(state, TimeProvider.System, Serilog.Core.Logger.None);

		var services = new ServiceCollection();
		services.AddScoped<CapabilityInvocationContextHolder>();
		services.AddSingleton<IHostInvoker>(hostInvoker);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		var handler = new TestCapabilityHandler("actions",
			async (invocation, cancellationToken) =>
			{
				// The handler calls back into the host and genuinely awaits the reply - exactly what
				// RemoteActionInteractions / the Remote*Api adapters do from inside a capability
				// handler.
				var invoker = invocation.Services.GetRequiredService<IHostInvoker>();
				await invoker.InvokeAsync(HostApis.Variables, HostOperations.Variables.List, null, cancellationToken);
				return CapabilityInvocationResult.Ok();
			});

		using var dispatcher = new CapabilityDispatcher(new CapabilityCatalog([handler]),
			scopeFactory,
			state,
			TimeProvider.System,
			Serilog.Core.Logger.None);

		await using var socket = new FakePluginSocket();
		var connection = new PluginSessionConnection(socket,
			TestSession.Create(),
			dispatcher,
			state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			hostInvoker,
			hostStateCache: null);

		// Normally set by PluginConnectionHostedService once the handshake completes; set directly
		// here since this test drives the connection without that service.
		state.ActiveConnection = connection;

		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionWelcome,
			Id = "welcome",
			Payload = FakePluginSocket.Payload(new SessionWelcomePayload { SessionId = "session-1", Resumed = false })
		});

		var run = connection.RunAsync(null, "instance-1", CancellationToken.None);
		await socket.NextAsync(MessageTypes.SessionHello);

		// The host "invokes" the actions capability - the plugin now owes it a capability.result.
		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.CapabilityInvoke,
			Id = "invoke-1",
			Payload = FakePluginSocket.Payload(new MacroDeck.Plugin.Protocol.Capabilities.CapabilityInvokePayload
			{
				Kind = "actions", LocalId = "one", Operation = "execute"
			})
		});

		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		// While the host would be awaiting that capability.result, the plugin's handler is itself
		// awaiting a host.invoke reply. The host answers it here - this is the step that would never
		// happen if the deadlock regressed, and the bounded timeout above is what turns a regression
		// into a failing assertion instead of a hung test run.
		var hostInvoke = await socket.NextAsync(MessageTypes.HostInvoke, TimeSpan.FromSeconds(10));

		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.HostResult,
			Id = "result-1",
			CorrelationId = hostInvoke.Id,
			Payload = FakePluginSocket.Payload(new HostResultPayload())
		});

		var capabilityResult = await socket.NextAsync(MessageTypes.CapabilityResult, TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(capabilityResult.CorrelationId, Is.EqualTo("invoke-1"));
			Assert.That(capabilityResult.Error, Is.Null);
		});

		socket.CloseFromHost(1000);
		await run.WaitAsync(timeout.Token);
	}
}
