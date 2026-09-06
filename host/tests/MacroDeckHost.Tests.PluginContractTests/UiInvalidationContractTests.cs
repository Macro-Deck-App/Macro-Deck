using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// A plugin dying is the case the whole session concept exists for. The client must be told at once,
/// under one code, without waiting out any invoke budget - which is why the clock in this test never
/// moves.
/// </summary>
[TestFixture]
internal sealed class UiInvalidationContractTests : UiContractFixture
{
	private const string TreeAtThree =
		"{\"revision\":3,\"surface\":{\"kind\":\"config\",\"sessionMode\":\"shared\",\"attributes\":{}}," +
		"\"root\":{\"id\":\"root\",\"type\":\"panel\",\"properties\":{},\"children\":[]}}";

	[Test]
	public async Task Killing_an_out_of_process_plugin_mid_session_invalidates_every_client_once_and_immediately()
	{
		var handler = new TestUiCapabilityHandler
		{
			SessionMode = UiSessionModes.Shared,
			OnSnapshotRequested = sessionId => Link.SendRawFromPluginAsync(UiWire.Snapshot(sessionId, TreeAtThree))
		};

		await ConnectAsync([handler],
			[
				new DeclaredCapability
				{
					Kind = CapabilityKinds.Ui,
					LocalId = ProviderCapabilityId.LocalId,
					VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
				}
			],
			[CapabilityKinds.Ui]);

		var ticket = await Broker.OpenAsync(PluginId,
			new UiSurface { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Shared },
			OwnerPrincipal,
			CancellationToken.None);

		var sessionId = ticket.SessionId;

		var first = Attach(sessionId, "c1");
		var second = Attach(sessionId, "c2");
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count >= 1 &&
				UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c2").Count >= 1,
			"Both clients never received a tree, so nothing proves they were live.");

		// The plugin will never answer this one.
		Link.DropNextReply();
		var call = Task.Run(() => Broker.SendEvent(new UiSendEventRequest
				{ SessionId = sessionId, NodeId = "root", Name = "click", Revision = 3 },
			"c1"));

		await WaitForUiAsync(() => handler.Events.Count == 1, "The event never reached the plugin.");

		// Exactly what the runtime does: the connection goes away and the session registry says so.
		Disconnect();

		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionInvalidatedEvent>("c1").Count == 1 &&
				UiTransport.MessagesFor<UiSessionInvalidatedEvent>("c2").Count == 1,
			"A dead plugin left its clients rendering a tree nothing will ever update again.");
		await SettleAsync();

		var finished = await Task.WhenAny(call, Task.Delay(TimeSpan.FromSeconds(2)));
		var afterDeath = Attach(sessionId, "c3");
		var messagesBefore = UiTransport.All.Count;

		Disconnect();
		await SettleAsync();

		var invalidations = UiTransport.All
			.Select(recorded => recorded.Message)
			.OfType<UiSessionInvalidatedEvent>()
			.ToArray();

		Assert.Multiple(() =>
		{
			Assert.That(first.Accepted, Is.True);
			Assert.That(second.Accepted, Is.True);
			Assert.That(invalidations, Has.Length.EqualTo(2), "The death was reported more than once per client.");

			foreach (var invalidation in invalidations)
			{
				Assert.That(invalidation.Code, Is.EqualTo(UiSessionErrorCodes.ProviderDisconnected));
				Assert.That(invalidation.Retryable, Is.True);
				Assert.That(invalidation.Message, Is.Not.Empty);
			}

			foreach (var connectionId in new[] { "c1", "c2" })
			{
				var ordered = UiTransport.For(connectionId).Select(recorded => recorded.Message).ToArray();
				var invalidatedAt = Array.FindIndex(ordered, message => message is UiSessionInvalidatedEvent);
				Assert.That(ordered.Take(invalidatedAt),
					Has.None.InstanceOf<UiSessionClosedEvent>(),
					$"{connectionId} was told the session closed before it was told the provider died.");
			}

			Assert.That(finished,
				Is.SameAs(call),
				"A hub call was still waiting on a plugin that had already gone away.");
			Assert.That(call.Result.Accepted, Is.True, "The in-flight call produced no definite answer.");
			Assert.That(afterDeath.Accepted, Is.False);
			Assert.That(afterDeath.Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
			Assert.That(UiTransport.All,
				Has.Count.EqualTo(messagesBefore),
				"Dropping an already-dead link produced more messages.");
		});

		// Five minutes later there must still be no second verdict on the same death.
		Time.Advance(TimeSpan.FromMinutes(5));
		await SettleAsync();

		Assert.That(UiTransport.All.Select(recorded => recorded.Message).OfType<UiSessionInvalidatedEvent>(),
			Has.Exactly(2).Items,
			"The same death was reported a second time under a timeout code.");
	}
}
