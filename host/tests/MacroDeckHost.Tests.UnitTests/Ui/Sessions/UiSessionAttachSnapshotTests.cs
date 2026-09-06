using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// Attaching asks the provider for a tree, and so does opening. A client that attaches while the
/// opening snapshot is still in flight is already served by it - the pending attachments are read as a
/// snapshot is delivered, not as it was asked for - so a second request only puts the same tree in
/// front of everyone attached twice. What must not follow from skipping it is a client left with no
/// tree, which is why the late-join case is asserted alongside.
/// </summary>
[TestFixture]
internal sealed class UiSessionAttachSnapshotTests : UiSessionFixture
{
	[Test]
	public async Task Attaching_while_the_opening_snapshot_is_in_flight_delivers_one_tree()
	{
		var release = new TaskCompletionSource();
		var provider = AddProvider();
		provider.Snapshot = () =>
		{
			release.Task.GetAwaiter().GetResult();
			return UiPayloads.Tree(1);
		};

		var ticket = Broker.Open(ProviderId, Surface(UiSessionModes.Shared), DeviceA);
		Assert.That(ticket.Accepted, Is.True);
		await WaitForAsync(() => provider.SnapshotRequests == 1, "The opening snapshot was never requested.");

		var attach = Attach(ticket.SessionId, "c1");
		release.SetResult();
		await WaitForMessagesAsync("c1", 1, "The attaching client never got a tree.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(attach.Accepted, Is.True);
			Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c1"),
				Has.Count.EqualTo(1),
				"The same tree was put in front of the client twice.");
			Assert.That(provider.SnapshotRequests,
				Is.EqualTo(1),
				"The provider was asked to build a tree it had already been asked for.");
		});
	}

	/// <summary>
	/// The order a real client produces, and the one the first test does not: opening only queues the
	/// provider call, so the attach that follows on its own connection reaches the broker first and is
	/// the one that asks for the tree. Whichever of the two finds a request already outstanding has to
	/// be the one that stands down - guarding only the attach left the opening request duplicating every
	/// modal's tree.
	/// </summary>
	[Test]
	public async Task Attaching_before_the_provider_finished_opening_delivers_one_tree()
	{
		var provider = AddProvider();
		provider.Gate = new TaskCompletionSource();

		var ticket = Broker.Open(ProviderId, Surface(UiSessionModes.Shared), DeviceA);
		var attach = Attach(ticket.SessionId, "c1");
		Assert.That(provider.SnapshotRequests, Is.Zero, "The provider answered while it was still opening.");

		provider.Gate.SetResult();
		await WaitForMessagesAsync("c1", 1, "The attaching client never got a tree.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(attach.Accepted, Is.True);
			Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c1"),
				Has.Count.EqualTo(1),
				"The same tree was put in front of the client twice.");
			Assert.That(provider.SnapshotRequests,
				Is.EqualTo(1),
				"The provider was asked to build a tree it had already been asked for.");
		});
	}

	[Test]
	public async Task Attaching_after_a_tree_was_delivered_still_gets_one()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, UiSessionModes.Shared);

		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The first client never got a tree.");

		Attach(sessionId, "c2");
		await WaitForMessagesAsync("c2", 1, "A client that joined later was left with no tree.");

		Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c2"), Has.Count.GreaterThanOrEqualTo(1));
	}
}
