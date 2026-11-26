using System.Text.Json;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

[TestFixture]
internal sealed class UiHostObserverTests : UiSessionFixture
{
	[Test]
	public async Task An_observer_is_handed_the_tree_every_patch_and_the_end()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		var observer = new RecordingObserver();

		using var observation = Broker.ObserveHost(sessionId, observer);
		await WaitForAsync(() => observer.Snapshots.Count == 1, "The observer was never handed a tree.");
		var patch = Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(UiPayloads.Patch(1, 2)));
		await Broker.CloseAsync(sessionId, "done", CancellationToken.None);
		await WaitForAsync(() => observer.Ended.Count == 1, "The observer never heard the session end.");

		Assert.Multiple(() =>
		{
			Assert.That(observation, Is.Not.Null);
			Assert.That(patch.Accepted, Is.True);
			Assert.That(observer.Patches, Is.EqualTo(new[] { (1, 2) }));
			Assert.That(observer.Ended[0], Is.EqualTo(UiSessionEndReason.Closed));
		});
	}

	[Test]
	public async Task A_session_observed_by_the_host_outlives_the_grace_for_a_session_no_client_attached_to()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		var observer = new RecordingObserver();
		using var observation = Broker.ObserveHost(sessionId, observer);
		await WaitForAsync(() => observer.Snapshots.Count == 1, "The observer was never handed a tree.");

		Time.Advance(TimeSpan.FromMinutes(5));
		Broker.SweepDraining();

		Assert.That(Registry.Find(sessionId), Is.Not.Null);
	}

	[Test]
	public async Task A_client_attached_beside_an_observer_sees_one_tree_and_one_patch()
	{
		AddProvider();
		var ticket = Broker.Open(ProviderId, Surface(), DeviceA);
		var observer = new RecordingObserver();
		using var observation = Broker.ObserveHost(ticket.SessionId, observer);
		await ticket.Ready;
		Attach(ticket.SessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its tree.");

		Broker.PublishPatch(ProviderId, ticket.SessionId, new UiRawJson(UiPayloads.Patch(1, 2)));
		await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>("c1").Count == 1, "The patch never arrived.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c1"), Has.Count.EqualTo(1));
			Assert.That(MessagesFor<UiSessionPatchedEvent>("c1"), Has.Count.EqualTo(1));
			Assert.That(observer.Patches, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task The_host_s_tree_follows_patches_and_one_it_cannot_apply_brings_a_fresh_tree()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		var trees = new List<JsonElement>();
		using var tree = UiHostTree.Observe(Broker,
			sessionId,
			document =>
			{
				lock (trees)
				{
					trees.Add(document);
				}
			},
			(_, _, _) =>
			{
			},
			Serilog.Core.Logger.None);

		JsonElement? Latest()
		{
			lock (trees)
			{
				return trees.Count == 0 ? null : trees[^1];
			}
		}

		await WaitForAsync(() => Latest() is not null, "The host never held a tree.");

		Broker.PublishPatch(ProviderId,
			sessionId,
			new UiRawJson(UiPayloads.Patch(1,
				2,
				UiPayloads.InsertOperation(UiPayloads.Node("added"), "added"))));
		await WaitForAsync(() => Ids(Latest()).Contains("added"), "The applied patch is missing.");
		var requested = provider.SnapshotRequests;

		provider.Snapshot = () => UiPayloads.Tree(7, 1);
		var rejected = Broker.PublishPatch(ProviderId,
			sessionId,
			new UiRawJson(UiPayloads.Patch(2, 3, """{"op":"remove-node","nodeId":"missing"}""")));
		await WaitForAsync(() => Latest()?.GetProperty("revision").GetInt32() == 7,
			"The host never got a fresh tree.");

		Assert.Multiple(() =>
		{
			Assert.That(rejected.Accepted, Is.True, "the relay itself accepted it");
			Assert.That(provider.SnapshotRequests, Is.EqualTo(requested + 1));
			Assert.That(Ids(Latest()), Does.Contain("n0"));
		});
	}

	private static List<string> Ids(JsonElement? tree)
	{
		var ids = new List<string>();

		void Walk(JsonElement node)
		{
			ids.Add(node.GetProperty("id").GetString()!);
			if (node.TryGetProperty("children", out var children))
			{
				foreach (var child in children.EnumerateArray())
				{
					Walk(child);
				}
			}
		}

		if (tree is { } document)
		{
			Walk(document.GetProperty("root"));
		}

		return ids;
	}

	private sealed class RecordingObserver : IUiHostObserver
	{
		private readonly Lock _gate = new();

		public List<int> Snapshots { get; } = [];

		public List<(int From, int To)> Patches { get; } = [];

		public List<UiSessionEndReason> Ended { get; } = [];

		public void OnSnapshot(UiRawJson tree, int revision)
		{
			lock (_gate)
			{
				Snapshots.Add(revision);
			}
		}

		public void OnPatch(UiRawJson patch, int fromRevision, int toRevision)
		{
			lock (_gate)
			{
				Patches.Add((fromRevision, toRevision));
			}
		}

		public void OnEnded(UiSessionEndReason reason, string? code, bool retryable)
		{
			lock (_gate)
			{
				Ended.Add(reason);
			}
		}
	}
}
