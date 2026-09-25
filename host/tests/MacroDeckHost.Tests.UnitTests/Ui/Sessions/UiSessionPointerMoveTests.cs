using System.Text;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiSessionPointerMoveTests : UiSessionFixture
{
	private static readonly string[] _newestMovesInOrder = ["press", "m3", "up", "m5", "n2", "o2", "p1", "p2"];

	private static readonly string[] _newestInProcessMoves = ["press", "a2", "up", "a3", "x2", "b2"];

	private void Send(string sessionId,
		string connectionId,
		string nodeId,
		string name,
		string tag,
		string? clientId = null)
		=> Broker.SendEvent(new UiSendEventRequest
			{
				SessionId = sessionId,
				NodeId = nodeId,
				Name = name,
				Data = new UiRawJson(Encoding.UTF8.GetBytes("{\"tag\":\"" + tag + "\"}"))
			},
			connectionId,
			clientId);

	private static string Tag(UiSessionEventCommand command)
		=> Encoding.UTF8.GetString(command.Data.Utf8.Span).Split('"')[3];

	private static string Tag(UiEvent uiEvent) => uiEvent.Data!.Value.GetProperty("tag").GetString()!;

	private async Task<(StubUiSessionProvider Provider, string SessionId)> OpenSharedAsync()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider, UiSessionModes.Shared);
		Attach(sessionId, "c1");
		Attach(sessionId, "c2");
		await WaitForMessagesAsync("c2", 1, "The second client never got a tree.");

		return (provider, sessionId);
	}

	[Test]
	public async Task A_slow_provider_receives_the_newest_move_per_client_and_node_and_every_other_event_in_order()
	{
		var (provider, sessionId) = await OpenSharedAsync();
		provider.Gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		Send(sessionId, "c1", "pad", UiComponentEvents.Press, "press");
		await WaitForAsync(() => provider.Events.Count == 1, "The first event never reached the provider.");

		Send(sessionId, "c1", "pad", UiComponentEvents.PointerMove, "m1");
		Send(sessionId, "c1", "pad", UiComponentEvents.PointerMove, "m2");
		Send(sessionId, "c1", "pad", UiComponentEvents.PointerMove, "m3");
		Send(sessionId, "c1", "pad", UiComponentEvents.PointerUp, "up");
		Send(sessionId, "c1", "pad", UiComponentEvents.PointerMove, "m4");
		Send(sessionId, "c2", "pad", UiComponentEvents.PointerMove, "n1");
		Send(sessionId, "c1", "pad", UiComponentEvents.PointerMove, "m5");
		Send(sessionId, "c2", "pad", UiComponentEvents.PointerMove, "n2");
		Send(sessionId, "c1", "other", UiComponentEvents.PointerMove, "o1");
		Send(sessionId, "c1", "other", UiComponentEvents.PointerMove, "o2");
		Send(sessionId, "c1", "pad", UiComponentEvents.Press, "p1");
		Send(sessionId, "c1", "pad", UiComponentEvents.Press, "p2");

		provider.Gate.SetResult();
		await WaitForAsync(() => provider.Events.Count == 8, "The queued events never all reached the provider.");
		await SettleAsync();

		Assert.That(provider.Events.Select(Tag), Is.EqualTo(_newestMovesInOrder));
	}

	[Test]
	public async Task A_provider_that_keeps_up_receives_every_move()
	{
		var (provider, sessionId) = await OpenSharedAsync();

		for (var step = 0; step < 20; step++)
		{
			Send(sessionId, "c1", "pad", UiComponentEvents.PointerMove, "m" + step);
			await WaitForAsync(() => provider.Events.Count == step + 1, "A move the provider could keep up with was dropped.");
		}

		Assert.That(provider.Events.Select(Tag), Is.EqualTo(Enumerable.Range(0, 20).Select(step => "m" + step)));
	}

	[Test]
	public async Task A_slow_in_process_view_receives_the_newest_move_per_client_and_node_even_when_the_host_kept_up()
	{
		using var hold = new ManualResetEventSlim();
		var session = AddInProcessProvider(() => TreeAt(1, UiSessionModes.Shared), hold: hold);
		var sessionId = await OpenAsync(ProviderId, UiSessionModes.Shared);
		Attach(sessionId, "c1");
		Attach(sessionId, "c2");
		Attach(sessionId, "c3");

		Send(sessionId, "c1", "pad", UiComponentEvents.Press, "press", "a");
		await WaitForAsync(() => session.Dispatched.Count == 1, "The first event never reached the view.");

		Send(sessionId, "c1", "pad", UiComponentEvents.PointerMove, "a1", "a");
		await SettleAsync();
		Send(sessionId, "c1", "pad", UiComponentEvents.PointerMove, "a2", "a");
		await SettleAsync();
		Send(sessionId, "c1", "pad", UiComponentEvents.PointerUp, "up", "a");
		await SettleAsync();
		Send(sessionId, "c1", "pad", UiComponentEvents.PointerMove, "a3", "a");
		await SettleAsync();
		Send(sessionId, "c3", "pad", UiComponentEvents.PointerMove, "x1");
		await SettleAsync();
		Send(sessionId, "c3", "pad", UiComponentEvents.PointerMove, "x2");
		await SettleAsync();
		Send(sessionId, "c2", "pad", UiComponentEvents.PointerMove, "b1", "b");
		await SettleAsync();
		Send(sessionId, "c2", "pad", UiComponentEvents.PointerMove, "b2", "b");
		await SettleAsync();

		hold.Set();
		await WaitForAsync(() => session.Dispatched.Count == 6, "The queued events never all reached the view.");
		await SettleAsync();

		Assert.That(session.Dispatched.Select(Tag), Is.EqualTo(_newestInProcessMoves));
	}
}
