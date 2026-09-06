using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// One death must be reported once, under one code, and must reach only the sessions it actually
/// killed. A client that is told two different things about the same failure, or is killed by a
/// failure that was not its provider's, is as bad for the renderer as never being told at all.
/// </summary>
[TestFixture]
internal sealed class UiSessionInvalidationTests : UiSessionFixture
{
	[Test]
	public async Task A_provider_call_that_fails_because_the_link_is_gone_is_reported_as_a_disconnection()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		// A dropped connection fails the in-flight call and ends the plugin session at the same moment.
		// Both reach the broker, so the code a client sees may not depend on which arrives first.
		provider.Fail = () => new UiProviderDisconnectedException("the link is gone");

		Broker.SendEvent(new UiSendEventRequest { SessionId = sessionId, NodeId = "root", Name = "click" }, "c1");

		await WaitForAsync(() => MessagesFor<UiSessionInvalidatedEvent>("c1").Count == 1,
			"A call that failed on a dead link left the session alive.");
		await SettleAsync();

		var invalidations = MessagesFor<UiSessionInvalidatedEvent>("c1");

		Assert.Multiple(() =>
		{
			Assert.That(invalidations, Has.Count.EqualTo(1));
			Assert.That(invalidations[0].Code,
				Is.EqualTo(UiSessionErrorCodes.ProviderDisconnected),
				"A failure caused by the disconnection was reported as a second, separate fault.");
			Assert.That(invalidations[0].Retryable, Is.True);
		});
	}

	[Test]
	public async Task A_provider_call_that_fails_for_any_other_reason_is_still_reported_as_a_fault()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		provider.Fail = () => new InvalidOperationException("boom");

		Broker.SendEvent(new UiSendEventRequest { SessionId = sessionId, NodeId = "root", Name = "click" }, "c1");

		await WaitForAsync(() => MessagesFor<UiSessionInvalidatedEvent>("c1").Count == 1,
			"A provider call that threw left the session alive.");

		Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1")[0].Code,
			Is.EqualTo(UiSessionErrorCodes.ProviderFaulted));
	}

	[Test]
	public async Task A_reconnecting_plugins_new_session_survives_the_old_connections_teardown()
	{
		var provider = AddProvider();

		var firstPluginSession = await RegisterPluginSessionAsync();
		var doomed = await OpenAsync(provider);
		Attach(doomed, "c1");
		await WaitForMessagesAsync("c1", 1, "The first session's client never received a tree.");

		// The plugin reconnects: a second plugin session replaces the first, and the UI session opened
		// over the old link dies with it.
		await RegisterPluginSessionAsync();
		await WaitForAsync(() => MessagesFor<UiSessionInvalidatedEvent>("c1").Count == 1,
			"The session served by the replaced connection was not invalidated.");

		var reopened = await OpenAsync(provider);
		Attach(reopened, "c2");
		await WaitForMessagesAsync("c2", 1, "The reopened session's client never received a tree.");

		// The old connection's teardown lands last, naming the session it belonged to. It must not touch
		// the session the reconnect opened - the same ordering PluginWebSocketEndpoint guards the asset
		// path against with IsCurrentConnection.
		PluginSessions.RaiseSessionEnded(ProviderId, firstPluginSession, PluginSessionEndReason.Detached);
		await SettleAsync();

		Broker.PublishPatch(ProviderId, reopened, new UiRawJson(UiPayloads.Patch(1, 2)));
		await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>("c2").Count == 1,
			"The reopened session stopped relaying after the old connection's teardown.");

		Assert.Multiple(() =>
		{
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c2"),
				Is.Empty,
				"A reconnecting plugin's new session was killed by the old connection's teardown.");
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"), Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task A_provider_that_stops_answering_resync_requests_becomes_a_timeout()
	{
		var provider = AddProvider(treeRevision: 1);
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		// From here the provider records resync requests and answers none of them. Nothing else about it
		// changes - it is still connected, still not faulting, and no hub call is pending on it.
		provider.Snapshot = null;

		var snapshotsBefore = provider.SnapshotRequests;

		// A patch the host cannot apply asks for a fresh tree, which is the request that never lands.
		Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(5, 6)));
		await WaitForAsync(() => provider.SnapshotRequests > snapshotsBefore,
			"An inapplicable patch never asked the provider for a fresh tree.");

		Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"),
			Is.Empty,
			"A provider was declared dead before the clock said so.");

		Time.Advance(ProtocolTimeouts.CapabilityInvoke);

		await WaitForAsync(() => MessagesFor<UiSessionInvalidatedEvent>("c1").Count == 1,
			"A provider that answered the opening snapshot and then went quiet left the client stale forever.");
		await SettleAsync();

		var invalidations = MessagesFor<UiSessionInvalidatedEvent>("c1");

		Assert.Multiple(() =>
		{
			Assert.That(invalidations, Has.Count.EqualTo(1));
			Assert.That(invalidations[0].Code, Is.EqualTo(UiSessionErrorCodes.ProviderTimeout));
			Assert.That(invalidations[0].Retryable, Is.True);
			Assert.That(Attach(sessionId, "c2").Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
		});
	}

	[Test]
	public async Task An_answered_resync_does_not_leave_a_deadline_that_kills_the_session_later()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		provider.Snapshot = () => UiPayloads.Tree(6);
		Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(5, 6)));
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 2,
			"The resync never landed.");

		Time.Advance(ProtocolTimeouts.CapabilityInvoke * 3);
		await SettleAsync();

		Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"),
			Is.Empty,
			"A snapshot deadline outlived the snapshot that satisfied it.");
	}
}
