using System.Diagnostics;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// ADR 0062: no hub-facing broker method waits on a provider. A provider that has stopped answering
/// without disconnecting is the hard case - there is no socket close to react to, so the only thing
/// keeping the client responsive is that nothing ever awaited the provider in the first place.
/// </summary>
[TestFixture]
internal sealed class UiSessionNonBlockingTests : UiSessionFixture
{
	private static readonly TimeSpan _bound = TimeSpan.FromSeconds(2);

	[Test]
	public async Task A_silent_provider_never_blocks_a_hub_method_and_times_out_only_when_the_clock_advances()
	{
		var stuck = AddProvider();
		stuck.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		var ticket = Broker.Open(stuck.ProviderId, Surface(), DeviceA);
		Assert.That(ticket.Accepted, Is.True);

		var sessionId = ticket.SessionId;
		var stopwatch = Stopwatch.StartNew();

		var firstAttach = Attach(sessionId, "c1");
		var sent = Broker.SendEvent(new UiSendEventRequest { SessionId = sessionId, NodeId = "root", Name = "click" },
			"c1");
		Broker.Detach(sessionId, "c1");
		var secondAttach = Attach(sessionId, "c1");

		stopwatch.Stop();

		// A healthy provider is unaffected by the stuck one - the clock has not moved, so a shared
		// deadline would have starved this one too.
		var healthy = AddProvider("com.example.other", treeRevision: 7);
		var healthySessionId = await OpenAsync(healthy);
		var healthyAttach = Attach(healthySessionId, "c2");
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c2").Count == 1,
			"A stuck provider stalled an unrelated session.");

		Assert.Multiple(() =>
		{
			Assert.That(stopwatch.Elapsed,
				Is.LessThan(_bound),
				"A hub-facing method waited on a provider that never answered.");
			Assert.That(firstAttach.Accepted, Is.True);
			Assert.That(secondAttach.Accepted, Is.True);
			Assert.That(sent.Accepted, Is.True);
			Assert.That(ticket.Ready.IsCompleted, Is.False, "The provider answered after all.");
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"),
				Is.Empty,
				"A silent provider was declared dead before the clock said so.");
			Assert.That(healthyAttach.Revision, Is.EqualTo(7));
		});

		Time.Advance(ProtocolTimeouts.CapabilityInvoke);
		await WaitForAsync(() => MessagesFor<UiSessionInvalidatedEvent>("c1").Count == 1,
			"A provider that never answered never became a timeout.");
		await SettleAsync();

		var invalidations = MessagesFor<UiSessionInvalidatedEvent>("c1");

		Assert.Multiple(() =>
		{
			Assert.That(invalidations, Has.Count.EqualTo(1));
			Assert.That(invalidations[0].Code, Is.EqualTo(UiSessionErrorCodes.ProviderTimeout));
			Assert.That(invalidations[0].Retryable, Is.True);
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c2"), Is.Empty);
		});

		stuck.Gate.TrySetResult();
	}
}
