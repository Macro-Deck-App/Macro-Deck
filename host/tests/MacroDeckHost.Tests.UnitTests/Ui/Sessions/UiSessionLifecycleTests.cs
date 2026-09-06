using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiSessionLifecycleTests : UiSessionFixture
{
	[Test]
	public async Task Opening_a_session_creates_it_without_attaching_anyone_and_pushes_nothing()
	{
		var provider = AddProvider();

		var sessionId = await OpenAsync(provider);
		await SettleAsync();

		Assert.That(Transport.All, Is.Empty, "Opening a session pushed a message to a client that never attached.");

		// The provider produces a second tree while nobody is attached. A relay that broadcast to
		// Clients.All, or fanned out to a group nobody has joined, would surface here and nowhere else.
		Broker.PublishSnapshot(ProviderId, sessionId, new UiRawJson(UiPayloads.Tree(1)));
		await SettleAsync();

		Assert.That(Transport.All,
			Is.Empty,
			"A provider snapshot for an unattached session was pushed to clients anyway.");

		var attached = Attach(sessionId, "c1");

		Assert.Multiple(() =>
		{
			Assert.That(attached.Accepted, Is.True);
			Assert.That(attached.Revision, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_successful_attach_leaves_the_client_holding_a_tree_at_the_revision_the_response_reported()
	{
		var provider = AddProvider(treeRevision: 4);
		var sessionId = await OpenAsync(provider);

		var attached = Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The attaching client never received a tree.");
		await SettleAsync();

		var trees = MessagesFor<UiSessionTreeUpdatedEvent>("c1");

		Assert.Multiple(() =>
		{
			Assert.That(attached.Accepted, Is.True);
			Assert.That(attached.Revision, Is.EqualTo(4));
			Assert.That(attached.SurfaceKind, Is.EqualTo(UiSurfaceKinds.Config));
			Assert.That(attached.SessionMode, Is.EqualTo(UiSessionModes.Exclusive));
			Assert.That(trees, Has.Count.EqualTo(1), "The reported revision was not backed by a delivered tree.");
			Assert.That(trees[0].Revision, Is.EqualTo(4));
			Assert.That(MessagesFor("c1"), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Detaching_stops_delivery_to_that_connection_only()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, UiSessionModes.Shared);

		Attach(sessionId, "c1");
		Attach(sessionId, "c2");
		await WaitForMessagesAsync("c1", 1, "c1 never received its tree.");
		await WaitForMessagesAsync("c2", 1, "c2 never received its tree.");

		var beforeDetach = MessagesFor("c1").Count;
		Broker.Detach(sessionId, "c1");
		await SettleAsync();

		Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(1, 2)));
		await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>("c2").Count == 1,
			"c2 never received the patch that followed c1's detach.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(MessagesFor("c1"),
				Has.Count.EqualTo(beforeDetach),
				"A detached connection was still being delivered to.");
			Assert.That(MessagesFor<UiSessionPatchedEvent>("c2"), Has.Count.EqualTo(1));
			Assert.That(provider.CloseCalls, Is.Zero, "Detaching one of two clients closed the session.");
		});
	}

	[Test]
	public async Task Closing_a_session_notifies_every_attached_client_exactly_once_and_closes_it_at_the_provider()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, UiSessionModes.Shared);

		Attach(sessionId, "c1");
		Attach(sessionId, "c2");
		await WaitForMessagesAsync("c1", 1, "c1 never received its tree.");
		await WaitForMessagesAsync("c2", 1, "c2 never received its tree.");

		await Broker.CloseAsync(sessionId, "done", CancellationToken.None);
		await WaitForAsync(() => MessagesFor<UiSessionClosedEvent>("c1").Count == 1 &&
				MessagesFor<UiSessionClosedEvent>("c2").Count == 1,
			"A closed session left a client with no terminal message.");
		await WaitForAsync(() => provider.CloseCalls == 1, "The provider was never told the session closed.");
		await SettleAsync();

		var lateAttach = Attach(sessionId, "c3");

		var closedAgain = Broker.CloseAsync(sessionId, "done", CancellationToken.None);
		await closedAgain;
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(MessagesFor<UiSessionClosedEvent>("c1"), Has.Count.EqualTo(1));
			Assert.That(MessagesFor<UiSessionClosedEvent>("c2"), Has.Count.EqualTo(1));
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"), Is.Empty);
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c2"), Is.Empty);
			Assert.That(provider.CloseCalls, Is.EqualTo(1));
			Assert.That(lateAttach.Accepted, Is.False);
			Assert.That(lateAttach.Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
			Assert.That(MessagesFor("c3"), Is.Empty);
		});
	}

	[Test]
	public async Task Last_detach_starts_a_fifteen_second_drain_rather_than_closing_the_session()
	{
		var provider = AddProvider(treeRevision: 3);
		var sessionId = await OpenAsync(provider);

		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "c1 never received its tree.");

		Broker.Detach(sessionId, "c1");
		Time.Advance(TimeSpan.FromMilliseconds(14_999));
		await SettleAsync();

		Assert.That(provider.CloseCalls, Is.Zero, "The session closed before its grace window elapsed.");

		var reattached = Attach(sessionId, "c1");
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 2,
			"Re-attaching within the grace window never redelivered the tree.");

		Time.Advance(TimeSpan.FromSeconds(60));
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(reattached.Accepted, Is.True);
			Assert.That(reattached.Revision,
				Is.EqualTo(3),
				"Re-attaching restarted the session instead of resuming it.");
			Assert.That(provider.OpenCalls, Is.EqualTo(1), "Resuming a draining session re-opened it at the provider.");
			Assert.That(provider.CloseCalls, Is.Zero);
		});
	}

	[Test]
	public async Task A_session_left_draining_for_the_full_grace_window_closes_itself()
	{
		var provider = AddProvider(treeRevision: 3);
		var sessionId = await OpenAsync(provider);

		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "c1 never received its tree.");

		Broker.Detach(sessionId, "c1");
		Time.Advance(TimeSpan.FromSeconds(15));
		await WaitForAsync(() => provider.CloseCalls == 1, "The grace window elapsed without closing the session.");

		var lateAttach = Attach(sessionId, "c1");

		Time.Advance(TimeSpan.FromSeconds(60));
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(provider.CloseCalls, Is.EqualTo(1), "The session was closed at the provider more than once.");
			Assert.That(lateAttach.Accepted, Is.False);
			Assert.That(lateAttach.Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
		});
	}

	[Test]
	public async Task Detaching_one_of_several_shared_clients_does_not_start_the_drain()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, UiSessionModes.Shared);

		Attach(sessionId, "c1");
		Attach(sessionId, "c2");
		await WaitForMessagesAsync("c1", 1, "c1 never received its tree.");
		await WaitForMessagesAsync("c2", 1, "c2 never received its tree.");

		Broker.Detach(sessionId, "c1");
		Time.Advance(TimeSpan.FromSeconds(15));
		await SettleAsync();

		Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(1, 2)));
		await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>("c2").Count == 1,
			"The session was drained even though a client was still attached.");

		Assert.That(provider.CloseCalls, Is.Zero);
	}
}
