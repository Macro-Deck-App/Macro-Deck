using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class UiSessionBurstContractTests : UiContractFixture
{
	private const int TileCount = ProtocolLimits.MaxConcurrentInvocations + 13;

	private const string WidgetTree =
		"{\"revision\":1,\"surface\":{\"kind\":\"widget\",\"sessionMode\":\"shared\",\"attributes\":{}},"
		+ "\"root\":{\"id\":\"root\",\"type\":\"panel\",\"properties\":{},\"children\":[]}}";

	private static DeclaredCapability Provider()
	{
		return new DeclaredCapability
		{
			Kind = CapabilityKinds.Ui,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};
	}

	[Test]
	public async Task Every_tile_of_a_deck_that_opens_more_sessions_than_the_invocation_limit_renders()
	{
		var limitReached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var opening = 0;

		var handler = new TestUiCapabilityHandler
		{
			SurfaceKind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			OnSnapshotRequested = sessionId => Link.SendRawFromPluginAsync(UiWire.Snapshot(sessionId, WidgetTree))
		};

		handler.OnOpening = async () =>
		{
			if (Interlocked.Increment(ref opening) >= ProtocolLimits.MaxConcurrentInvocations)
			{
				limitReached.TrySetResult();
			}

			await limitReached.Task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
		};

		await ConnectAsync([handler], [Provider()], [CapabilityKinds.Ui]);

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Widget,
			SessionMode = UiSessionModes.Shared,
			Attributes = new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		};

		var tickets = await Task.WhenAll(Enumerable.Range(0, TileCount)
				.Select(_ => Broker.OpenAsync(PluginId, surface, OwnerPrincipal, CancellationToken.None)))
			.WaitAsync(TimeSpan.FromSeconds(30));

		Assert.That(tickets.Where(ticket => !ticket.Accepted).Select(ticket => ticket.Code),
			Is.Empty,
			"A session of the burst was refused instead of waiting for a free invocation slot.");

		var sessionIds = tickets.Select(ticket => ticket.SessionId).ToList();
		foreach (var sessionId in sessionIds)
		{
			Attach(sessionId, "client-" + sessionId);
		}

		await WaitForUiAsync(() => sessionIds.All(sessionId
				=> UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("client-" + sessionId).Count == 1),
			"A tile of the burst never received its tree.");

		Assert.That(handler.Opened, Has.Count.EqualTo(TileCount));
	}
}
