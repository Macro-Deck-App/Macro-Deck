using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// The in-process half of the "one session API serves both provider kinds" requirement. Nothing here
/// involves a plugin, and a plugin must not be involved: a built-in integration that serves a view has
/// no session on the plugin socket to invoke over.
/// </summary>
[TestFixture]
internal sealed class InProcessUiSessionTests : UiSessionFixture
{
	[Test]
	public async Task An_in_process_provider_serves_a_session_with_no_plugin_session_registered()
	{
		var revision = 1;
		var session = AddInProcessProvider(() => TreeAt(revision));

		var sessionId = await OpenAsync(ProviderId);

		var attached = Attach(sessionId, "c1");
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The in-process provider's tree never reached the client.");

		revision = 2;
		session.Emit(new UiPatch
		{
			FromRevision = 1,
			ToRevision = 2,
			Operations = [new UiPatchOperation { Op = UiPatchOperations.SetProperties, NodeId = "root" }]
		});
		await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>("c1").Count == 1,
			"A patch from the in-process provider never reached the client.");

		var sent = Broker.SendEvent(new UiSendEventRequest { SessionId = sessionId, NodeId = "root", Name = "click" },
			"c1");
		await WaitForAsync(() => session.Dispatched.Count == 1, "The client event never reached the provider.");

		await Broker.CloseAsync(sessionId, "done", CancellationToken.None);
		await WaitForAsync(() => MessagesFor<UiSessionClosedEvent>("c1").Count == 1,
			"Closing the session never reached the client.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(attached.Accepted, Is.True);
			Assert.That(sent.Accepted, Is.True);
			Assert.That(session.DisposeCalls,
				Is.EqualTo(1),
				"Closing the session did not dispose the provider's session exactly once.");
			Assert.That(PluginSessions.Snapshot(), Is.Empty, "The flow needed a plugin session to exist.");
			Assert.That(Invoker.Invocations, Is.Empty, "An in-process session produced capability.invoke traffic.");
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"), Is.Empty);
		});
	}

	[Test]
	public async Task An_in_process_provider_that_faults_mid_session_invalidates_the_client_with_the_same_shape()
	{
		var session = AddInProcessProvider(() => TreeAt(1));
		var sessionId = await OpenAsync(ProviderId);

		Attach(sessionId, "c1");
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received its tree.");

		Assert.DoesNotThrow(session.Fault, "A provider fault escaped into the broker's caller.");

		// StubUiSession.Fault raises the SDK event with this reason. It is the only untrusted text this
		// variant produces, so it is the only text an assertion here can prove was not relayed.
		await AssertFaultedAsync(sessionId, "c1", session, "gone");
	}

	[Test]
	public async Task An_in_process_provider_whose_tree_build_throws_invalidates_the_client_with_the_same_shape()
	{
		var explode = false;
		var session = AddInProcessProvider(() => explode
			? throw new InvalidOperationException("boom in UiViewRenderer at line 42")
			: TreeAt(1));

		var sessionId = await OpenAsync(ProviderId);

		Attach(sessionId, "c1");
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received its tree.");

		explode = true;
		Assert.DoesNotThrow(() => session.Emit(new UiPatch
			{
				FromRevision = 1,
				ToRevision = 2,
				Operations = [new UiPatchOperation { Op = UiPatchOperations.SetProperties, NodeId = "root" }]
			}),
			"A provider throw escaped into the broker's caller.");

		// The patch itself is fine; the next tree build is what throws.
		Broker.Detach(sessionId, "c1");
		Attach(sessionId, "c1");

		await AssertFaultedAsync(sessionId, "c1", session, "boom in UiViewRenderer at line 42");
	}

	[Test]
	public async Task A_forwarded_patch_reaches_the_client_as_the_exact_bytes_the_serializer_produced()
	{
		var session = AddInProcessProvider(() => TreeAt(1));
		var sessionId = await OpenAsync(ProviderId);

		Attach(sessionId, "c1");
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received its tree.");

		var patch = new UiPatch
		{
			FromRevision = 1,
			ToRevision = 2,
			Operations =
			[
				new UiPatchOperation
				{
					Op = UiPatchOperations.InsertNode,
					NodeId = "field.apiKey",
					ParentId = "root",
					Node = new UiNode { Id = "field.apiKey", Type = "text-field" }
				}
			]
		};

		session.Emit(patch);
		await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>("c1").Count == 1,
			"The patch never reached the client.");

		var delivered = Transport.For("c1").Single(recorded => recorded.Message is UiSessionPatchedEvent);

		// The in-process provider serialises with UiCanonicalJson, so adversarial bytes cannot originate
		// on this path. What is provable here is that whatever the serializer produced is what the client
		// receives - the relay adds no round trip of its own.
		Assert.That(delivered.Payload, Is.EqualTo(UiCanonicalJson.SerializeToUtf8Bytes(patch)));
	}

	private async Task AssertFaultedAsync(string sessionId,
		string connectionId,
		StubUiSession session,
		string providerText)
	{
		await WaitForAsync(() => MessagesFor<UiSessionInvalidatedEvent>(connectionId).Count >= 1,
			"A faulted provider left the client on a tree nothing will ever update again.");

		// The session object owns a pump and two event subscriptions. An invalidation that skipped the
		// close at the provider left both alive for the rest of the process, which is the leak
		// IUiProvider's "disposed when the session closes, including after a fault" promise forbids.
		await WaitForAsync(() => session.DisposeCalls == 1,
			"An invalidated session was never disposed at the provider.");
		await SettleAsync();

		var invalidations = MessagesFor<UiSessionInvalidatedEvent>(connectionId);
		var lateAttach = Attach(sessionId, "c-late");

		Assert.Multiple(() =>
		{
			Assert.That(invalidations, Has.Count.EqualTo(1));
			Assert.That(invalidations[0].Code, Is.EqualTo(UiSessionErrorCodes.ProviderFaulted));
			Assert.That(invalidations[0].Retryable, Is.True);
			Assert.That(invalidations[0].Message, Is.Not.Empty);
			Assert.That(invalidations[0].Message,
				Does.Not.Contain("InvalidOperationException"),
				"The provider's exception type was relayed to the client.");
			Assert.That(invalidations[0].Message,
				Does.Not.Contain(providerText),
				"The provider's own text was relayed to the client.");
			Assert.That(invalidations[0].Message,
				Does.Not.Contain("   at "),
				"A stack trace was relayed to the client.");
			Assert.That(lateAttach.Accepted, Is.False);
			Assert.That(lateAttach.Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
			Assert.That(session.DisposeCalls,
				Is.EqualTo(1),
				"The provider's session was disposed more than once.");
		});
	}
}
