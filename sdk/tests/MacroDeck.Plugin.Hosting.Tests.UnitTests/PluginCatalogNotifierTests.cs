using System.Text.Json;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// <see cref="PluginCatalogNotifier" /> - the <c>state.update</c> producer issue #413's remote
/// weather-location bug was missing entirely (<c>grep -rn "StateUpdate" sdk/src/</c> found nothing
/// before this). Mirrors <see cref="RemoteEventPublisher" />'s own test shape: a well-formed send when
/// connected, silence (never a throw, never a block) when not.
/// </summary>
[TestFixture]
public class PluginCatalogNotifierTests
{
	[Test]
	public void CatalogChanged_with_no_connection_neither_throws_nor_blocks()
	{
		var notifier = new PluginCatalogNotifier(new PluginConnectionState(),
			Serilog.Core.Logger.None);

		Assert.That(() => notifier.CatalogChanged(CapabilityKinds.Weather, "primary", "config changed"),
			Throws.Nothing);
	}

	[Test]
	public void CatalogChanged_with_a_blank_kind_is_a_no_op()
	{
		var notifier = new PluginCatalogNotifier(new PluginConnectionState(),
			Serilog.Core.Logger.None);

		Assert.That(() => notifier.CatalogChanged("  "), Throws.Nothing);
	}

	[Test]
	public async Task CatalogChanged_sends_a_well_formed_state_update()
	{
		var state = new PluginConnectionState();
		var notifier = new PluginCatalogNotifier(state, Serilog.Core.Logger.None);

		await using var socket = new FakePluginSocket();
		var connection = new PluginSessionConnection(socket,
			TestSession.Create(),
			TestSession.Dispatcher(),
			state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			hostInvoker: null,
			hostStateCache: null);

		// Normally set by PluginConnectionHostedService once the handshake completes; set directly here,
		// the same shape HostInvokerTests/DeadlockRegressionTests use to drive a connection without that
		// service.
		state.ActiveConnection = connection;

		socket.Push(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionWelcome,
			Id = "welcome",
			Payload = FakePluginSocket.Payload(new SessionWelcomePayload { SessionId = "session-1", Resumed = false })
		});

		var run = connection.RunAsync(null, "instance-1", CancellationToken.None);
		await socket.NextAsync(MessageTypes.SessionHello);

		notifier.CatalogChanged(CapabilityKinds.Weather, "primary", "config changed");

		var sent = await socket.NextAsync(MessageTypes.StateUpdate);
		var payload = sent.Payload!.Value.Deserialize<StateUpdatePayload>(PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(payload!.Kind, Is.EqualTo(CapabilityKinds.Weather));
			Assert.That(payload.LocalId, Is.EqualTo("primary"));
			Assert.That(payload.Reason, Is.EqualTo("config changed"));
		});

		socket.CloseFromHost(1000);
		await run;
	}
}
