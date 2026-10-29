using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiSessionRevisionContinuityTests : UiSessionFixture
{
	[Test]
	public async Task An_attached_client_keeps_a_contiguous_stream_when_a_late_join_snapshot_overtakes_a_patch()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, UiSessionModes.Shared);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The first client never received its tree.");

		provider.Snapshot = null;
		Attach(sessionId, "c2");
		await WaitForAsync(() => provider.SnapshotRequests == 3, "The late join never asked for a tree.");

		Broker.PublishSnapshot(ProviderId, sessionId, new UiRawJson(UiPayloads.Tree(2)));
		Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(1, 2)));
		Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(2, 3)));

		await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>("c1").Count == 1,
			"The first client never received the patch to revision 3.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(Gaps("c1"), Is.Empty, "The attached client was left behind a revision it never received.");
			Assert.That(Gaps("c2"), Is.Empty, "The late joiner was left behind a revision it never received.");
		});
	}

	[Test]
	public async Task A_late_join_at_the_attached_clients_revision_does_not_resend_them_the_tree()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, UiSessionModes.Shared);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The first client never received its tree.");

		Attach(sessionId, "c2");
		await WaitForMessagesAsync("c2", 1, "The late joiner never received its tree.");
		await SettleAsync();

		Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c1"),
			Has.Count.EqualTo(1),
			"An attached client was sent a tree it already held.");
	}

	private List<string> Gaps(string connectionId)
	{
		var gaps = new List<string>();
		int? held = null;

		foreach (var message in MessagesFor(connectionId))
		{
			switch (message)
			{
				case UiSessionTreeUpdatedEvent tree:
					held = tree.Revision;
					break;
				case UiSessionPatchedEvent patch when patch.FromRevision != held:
					gaps.Add($"patch {patch.FromRevision}->{patch.ToRevision} on top of {held}");
					held = patch.ToRevision;
					break;
				case UiSessionPatchedEvent patch:
					held = patch.ToRevision;
					break;
			}
		}

		return gaps;
	}
}
