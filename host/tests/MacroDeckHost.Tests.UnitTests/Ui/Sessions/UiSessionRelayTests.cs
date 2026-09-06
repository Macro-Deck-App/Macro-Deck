using System.Diagnostics;
using System.Text;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// The revision chain is what lets a client apply a patch at all. A patch that cannot apply, or one the
/// client has already applied, must never be delivered as though it did - and the client must never be
/// left holding a revision it can no longer advance from.
/// </summary>
[TestFixture]
internal sealed class UiSessionRelayTests : UiSessionFixture
{
	private const string BigNumber = "100000000000000000000000000000000000001";

	[Test]
	public async Task A_patch_whose_from_revision_does_not_match_the_session_revision_is_not_delivered()
	{
		var provider = AddProvider(treeRevision: 3);
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		provider.Snapshot = () => UiPayloads.Tree(6);
		var refused = Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(5, 6)));

		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 2,
			"An inapplicable patch left the client with nothing at all.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(refused.Accepted, Is.False);
			Assert.That(MessagesFor<UiSessionPatchedEvent>("c1"),
				Is.Empty,
				"A patch that could not apply was delivered as though it had.");
			Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c1")[^1].Revision, Is.EqualTo(6));
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"), Is.Empty);
		});
	}

	[Test]
	public async Task A_replayed_patch_is_delivered_at_most_once()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		var patch = UiPayloads.Patch(1, 2);
		Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(patch));
		await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>("c1").Count == 1,
			"The first patch was never delivered.");

		Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(patch));
		await SettleAsync();

		Broker.Detach(sessionId, "c1");
		var reattached = Attach(sessionId, "c1");

		Assert.Multiple(() =>
		{
			Assert.That(MessagesFor<UiSessionPatchedEvent>("c1"), Has.Count.EqualTo(1));
			Assert.That(reattached.Revision, Is.EqualTo(2), "A replayed patch advanced the session revision twice.");
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"), Is.Empty);
		});
	}

	[Test]
	public async Task A_patch_that_does_not_advance_the_revision_or_carries_no_operations_is_rejected()
	{
		var provider = AddProvider(treeRevision: 2);
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		var notAdvancing = Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(2, 2)));
		var noOperations = Broker.PublishPatch(ProviderId,
			sessionId,
			new UiRawJson(UiPayloads.PatchWithoutOperations(2, 3)));

		await SettleAsync();

		Broker.Detach(sessionId, "c1");
		var reattached = Attach(sessionId, "c1");

		Assert.Multiple(() =>
		{
			Assert.That(notAdvancing.Accepted, Is.False);
			Assert.That(notAdvancing.Code, Is.EqualTo(UiSessionErrorCodes.InvalidPayload));
			Assert.That(noOperations.Accepted, Is.False);
			Assert.That(noOperations.Code, Is.EqualTo(UiSessionErrorCodes.InvalidPayload));
			Assert.That(MessagesFor<UiSessionPatchedEvent>("c1"), Is.Empty);

			// The provider was told, and the session still stands where the client thinks it does.
			Assert.That(reattached.Revision, Is.EqualTo(2));
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"), Is.Empty);
		});
	}

	[Test]
	public async Task A_ui_event_reaches_the_provider_with_its_data_and_revision_intact()
	{
		var provider = AddProvider(treeRevision: 4);
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		// From here the provider never answers anything again.
		provider.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var data = Encoding.UTF8.GetBytes("{\"value\":\"x\",\"n\":" + BigNumber + "}");

		var stopwatch = Stopwatch.StartNew();
		var response = Broker.SendEvent(new UiSendEventRequest
			{
				SessionId = sessionId,
				NodeId = "field.apiKey",
				Name = "input",
				Data = new UiRawJson(data),
				Revision = 4
			},
			"c1");
		stopwatch.Stop();

		await WaitForAsync(() => provider.Events.Count == 1, "The event never reached the provider.");

		var dispatched = provider.Events[0];

		Assert.Multiple(() =>
		{
			Assert.That(response.Accepted, Is.True);
			Assert.That(stopwatch.Elapsed,
				Is.LessThan(TimeSpan.FromSeconds(2)),
				"Sending an event waited on the provider's handler.");
			Assert.That(dispatched.NodeId, Is.EqualTo("field.apiKey"));
			Assert.That(dispatched.Name, Is.EqualTo("input"));
			Assert.That(dispatched.Revision, Is.EqualTo(4));
			Assert.That(dispatched.Data.Utf8.ToArray(),
				Is.EqualTo(data),
				"The event payload was re-encoded on its way to the provider.");
		});

		provider.Gate.TrySetResult();
	}

	[Test]
	public async Task A_ui_event_for_an_unknown_or_closed_session_is_rejected_without_reaching_any_provider()
	{
		var provider = AddProvider();
		var live = await OpenAsync(provider);
		Attach(live, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		var closed = await OpenAsync(provider);
		Attach(closed, "c1");
		await Broker.CloseAsync(closed, "done", CancellationToken.None);
		await SettleAsync();

		var eventsBefore = provider.Events.Count;

		UiSendEventResponse unknown = null!;
		UiSendEventResponse toClosed = null!;
		UiSendEventResponse notAttached = null!;

		Assert.DoesNotThrow(() =>
		{
			unknown = Broker.SendEvent(Event(Guid.CreateVersion7().ToString("N")), "c1");
			toClosed = Broker.SendEvent(Event(closed), "c1");
			notAttached = Broker.SendEvent(Event(live), "c-never-attached");
		});

		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(unknown.Accepted, Is.False);
			Assert.That(unknown.Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
			Assert.That(toClosed.Accepted, Is.False);
			Assert.That(toClosed.Code, Is.EqualTo(UiSessionErrorCodes.SessionClosed));
			Assert.That(notAttached.Accepted, Is.False);
			Assert.That(notAttached.Code, Is.EqualTo(UiSessionErrorCodes.SessionForbidden));
			Assert.That(provider.Events, Has.Count.EqualTo(eventsBefore), "A refused event reached the provider.");
		});
	}

	private static UiSendEventRequest Event(string sessionId)
		=> new() { SessionId = sessionId, NodeId = "root", Name = "click" };
}
