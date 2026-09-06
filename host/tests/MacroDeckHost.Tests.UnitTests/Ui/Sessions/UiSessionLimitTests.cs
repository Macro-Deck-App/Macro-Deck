using System.Text;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Ui.Sessions;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;

namespace MacroDeckHost.Tests.UnitTests.Ui.Sessions;

/// <summary>
/// Every limit here is enforced so that a client is never left rendering a tree nothing will update
/// again. A payload the host cannot relay therefore has to produce something: a fresh tree, a terminal
/// error, or a rejection the provider can see - never silence.
/// </summary>
[TestFixture]
internal sealed class UiSessionLimitTests : UiSessionFixture
{
	[Test]
	public async Task A_tree_at_the_node_limit_is_served_and_one_node_over_terminates_the_session()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		var atLimit = Broker.PublishSnapshot(ProviderId,
			sessionId,
			new UiRawJson(UiPayloads.Tree(2, extraNodes: ProtocolLimits.MaxUiNodesPerTree - 1)));
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 2,
			"A tree exactly at the node limit was not served.");

		var treesBefore = MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count;

		var overLimit = Broker.PublishSnapshot(ProviderId,
			sessionId,
			new UiRawJson(UiPayloads.Tree(3, extraNodes: ProtocolLimits.MaxUiNodesPerTree)));
		await WaitForAsync(() => MessagesFor<UiSessionInvalidatedEvent>("c1").Count == 1,
			"An oversize tree left the session alive on a revision that can never advance.");
		await SettleAsync();

		var invalidation = MessagesFor<UiSessionInvalidatedEvent>("c1").Single();
		var lateAttach = Attach(sessionId, "c2");

		Assert.Multiple(() =>
		{
			Assert.That(atLimit.Accepted, Is.True);
			Assert.That(overLimit.Accepted, Is.False);
			Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c1"),
				Has.Count.EqualTo(treesBefore),
				"The oversize tree was delivered anyway.");
			Assert.That(invalidation.Code, Is.EqualTo(UiSessionErrorCodes.PayloadTooLarge));
			Assert.That(invalidation.Retryable, Is.False);
			Assert.That(lateAttach.Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
		});
	}

	[Test]
	public async Task Nodes_inside_a_fallback_subtree_count_toward_the_node_limit()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		var children = string.Join(',',
			Enumerable.Range(0, ProtocolLimits.MaxUiNodesPerTree - 1).Select(index => UiPayloads.Node($"n{index}")));

		var atLimit = Broker.PublishSnapshot(ProviderId,
			sessionId,
			new UiRawJson(UiPayloads.TreeWithRoot(2,
				"{\"id\":\"root\",\"type\":\"panel\",\"properties\":{},\"children\":[" + children + "]}")));

		var oneFallbackOver = Broker.PublishSnapshot(ProviderId,
			sessionId,
			new UiRawJson(UiPayloads.TreeWithRoot(3,
				"{\"id\":\"root\",\"type\":\"panel\",\"properties\":{},\"children\":[" +
				children +
				"],\"fallback\":" +
				UiPayloads.Node("fb") +
				"}")));

		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(atLimit.Accepted, Is.True);
			Assert.That(oneFallbackOver.Accepted,
				Is.False,
				"A node hidden in a fallback subtree escaped the node limit.");
			Assert.That(oneFallbackOver.Code, Is.EqualTo(UiSessionErrorCodes.PayloadTooLarge));
		});
	}

	[Test]
	public async Task The_serialized_tree_limit_is_measured_in_utf8_bytes_not_characters()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		var atLimit = UiPayloads.TreeOfExactBytes(2, ProtocolLimits.MaxUiTreeBytes);
		var overLimit = UiPayloads.TreeOfExactBytes(3, ProtocolLimits.MaxUiTreeBytes + 1);

		var accepted = Broker.PublishSnapshot(ProviderId, sessionId, new UiRawJson(atLimit));
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 2,
			"A tree exactly at the byte limit was not served.");

		var refused = Broker.PublishSnapshot(ProviderId, sessionId, new UiRawJson(overLimit));
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(UiPayloads.CharCountOf(overLimit),
				Is.LessThan(ProtocolLimits.MaxUiTreeBytes),
				"The over-limit tree is over the limit in characters too, so it proves nothing about bytes.");
			Assert.That(accepted.Accepted, Is.True);
			Assert.That(refused.Accepted, Is.False);
			Assert.That(refused.Code, Is.EqualTo(UiSessionErrorCodes.PayloadTooLarge));
		});
	}

	[Test]
	public async Task A_patch_at_the_patch_limit_is_forwarded_and_one_byte_over_resyncs_rather_than_going_silent()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		var atLimit = PatchOfExactBytes(1, 2, ProtocolLimits.MaxUiPatchBytes);
		var accepted = Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(atLimit));
		await WaitForAsync(() => MessagesFor<UiSessionPatchedEvent>("c1").Count == 1,
			"A patch exactly at the byte limit was not forwarded.");

		var delivered = Transport.For("c1").Single(recorded => recorded.Message is UiSessionPatchedEvent);

		// The rejected patch's target revision is what the client must end up holding.
		provider.Snapshot = () => UiPayloads.Tree(3);
		var overLimit = PatchOfExactBytes(2, 3, ProtocolLimits.MaxUiPatchBytes + 1);
		var refused = Broker.PublishPatch(ProviderId, sessionId, new UiRawJson(overLimit));

		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 2,
			"An oversize patch left the client silently stale.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(accepted.Accepted, Is.True);
			Assert.That(delivered.Payload, Is.EqualTo(atLimit));
			Assert.That(refused.Accepted, Is.False);
			Assert.That(MessagesFor<UiSessionPatchedEvent>("c1"),
				Has.Count.EqualTo(1),
				"The oversize patch was forwarded anyway.");
			Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c1")[^1].Revision,
				Is.EqualTo(3),
				"The replacement tree did not carry the rejected patch's revision.");
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"), Is.Empty);
		});
	}

	[Test]
	public async Task An_oversize_replacement_snapshot_after_an_oversize_patch_terminates_the_session()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		provider.Snapshot = () => UiPayloads.TreeOfExactBytes(2, ProtocolLimits.MaxUiTreeBytes + 1);
		Broker.PublishPatch(ProviderId,
			sessionId,
			new UiRawJson(PatchOfExactBytes(1, 2, ProtocolLimits.MaxUiPatchBytes + 1)));

		await WaitForAsync(() => MessagesFor<UiSessionInvalidatedEvent>("c1").Count == 1,
			"A session that can neither patch nor snapshot was left alive and frozen.");

		Assert.Multiple(() =>
		{
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1")[0].Code,
				Is.EqualTo(UiSessionErrorCodes.PayloadTooLarge));
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1")[0].Retryable, Is.False);
		});
	}

	[Test]
	public async Task The_update_rate_limit_admits_one_burst_then_refills_at_the_declared_rate()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		// The provider records the resync request but does not answer it, so the burst accounting is not
		// perturbed by a replacement tree mid-test.
		provider.Snapshot = null;

		var revision = 1;
		var burst = PushPatches(sessionId, ProtocolLimits.MaxUiUpdateBurst, ref revision);
		var refusals = PushPatches(sessionId, 50, ref revision);

		Time.Advance(TimeSpan.FromSeconds(1));
		var afterOneSecond = PushPatches(sessionId, ProtocolLimits.MaxUiUpdatesPerSecond + 10, ref revision);

		Time.Advance(TimeSpan.FromSeconds(10));
		var afterIdle = PushPatches(sessionId, ProtocolLimits.MaxUiUpdateBurst + 50, ref revision);

		Assert.Multiple(() =>
		{
			Assert.That(burst.Accepted, Is.EqualTo(ProtocolLimits.MaxUiUpdateBurst));
			Assert.That(refusals.Accepted, Is.Zero);
			Assert.That(refusals.Codes,
				Is.All.EqualTo(UiSessionErrorCodes.RateLimited),
				"A refused update was dropped without telling the provider why.");
			Assert.That(afterOneSecond.Accepted,
				Is.EqualTo(ProtocolLimits.MaxUiUpdatesPerSecond),
				"One second of refill did not admit exactly one second's worth of updates.");
			Assert.That(afterIdle.Accepted,
				Is.EqualTo(ProtocolLimits.MaxUiUpdateBurst),
				"A long idle banked more than one burst.");
		});
	}

	[Test]
	public async Task A_provider_that_blows_its_update_budget_again_after_the_resync_landed_loses_the_session()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		var revision = 1;
		PushPatches(sessionId, ProtocolLimits.MaxUiUpdateBurst, ref revision);

		var resyncRevision = revision;
		provider.Snapshot = () => UiPayloads.Tree(resyncRevision);

		var firstRefusal = Broker.PublishPatch(sessionId, ref revision);
		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 2,
			"The resync the refused update asked for never landed.");

		// The bucket is still empty, so this refusal happens with the resync already delivered - the one
		// state in which a provider is out of second chances.
		var afterResync = Broker.PublishPatch(sessionId, ref revision);
		await WaitForAsync(() => MessagesFor<UiSessionInvalidatedEvent>("c1").Count == 1,
			"A provider that blew its budget again after a resync kept the session forever.");
		await SettleAsync();

		var invalidation = MessagesFor<UiSessionInvalidatedEvent>("c1").Single();

		Assert.Multiple(() =>
		{
			Assert.That(firstRefusal.Accepted, Is.False);
			Assert.That(firstRefusal.Code, Is.EqualTo(UiSessionErrorCodes.RateLimited));
			Assert.That(afterResync.Accepted, Is.False);
			Assert.That(afterResync.Code, Is.EqualTo(UiSessionErrorCodes.RateLimited));
			Assert.That(invalidation.Code, Is.EqualTo(UiSessionErrorCodes.RateLimited));
			Assert.That(invalidation.Retryable, Is.True);
			Assert.That(Attach(sessionId, "c2").Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
		});
	}

	[Test]
	public async Task The_update_rate_limit_is_per_session_not_per_provider()
	{
		var provider = AddProvider();
		var first = await OpenAsync(provider);
		var second = await OpenAsync(provider);

		provider.Snapshot = null;

		var firstRevision = 1;
		PushPatches(first, ProtocolLimits.MaxUiUpdateBurst, ref firstRevision);
		var exhausted = Broker.PublishPatch(first, ref firstRevision);

		var secondRevision = 1;
		var other = Broker.PublishPatch(second, ref secondRevision);

		Assert.Multiple(() =>
		{
			Assert.That(exhausted.Accepted, Is.False);
			Assert.That(other.Accepted, Is.True, "One busy session spent another session's update budget.");
		});
	}

	[Test]
	public async Task Opening_more_sessions_than_a_provider_may_hold_is_refused_without_disturbing_the_others()
	{
		var provider = AddProvider();

		var sessions = new List<string>();
		for (var index = 0; index < ProtocolLimits.MaxUiSessionsPerProvider; index++)
		{
			sessions.Add(await OpenAsync(provider));
		}

		var refused = Broker.Open(provider.ProviderId, Surface(), DeviceA);

		var stillWorks = Attach(sessions[0], "c1");
		await WaitForMessagesAsync("c1", 1, "An existing session stopped serving once the provider filled up.");

		Assert.Multiple(() =>
		{
			Assert.That(refused.Accepted, Is.False);
			Assert.That(refused.Code, Is.EqualTo(UiSessionErrorCodes.TooManySessions));
			Assert.That(stillWorks.Accepted, Is.True);
		});
	}

	[Test]
	public async Task A_resource_at_the_declared_byte_limit_is_served()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		// The limit is a declaration check: nothing in this phase moves resource bytes, and no asset seam
		// is wired into this fixture, so there is deliberately no "zero asset traffic" assertion here -
		// it could only ever be vacuous.
		var atLimit = Broker.PublishSnapshot(ProviderId,
			sessionId,
			new UiRawJson(TreeWithResource(2, ProtocolLimits.MaxUiResourceBytes, inFallback: false)));

		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 2,
			"A resource exactly at the declared limit was not served.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(atLimit.Accepted, Is.True);
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"), Is.Empty);
		});
	}

	/// <summary>
	/// One over the declared limit, in each of the three places a resource can appear. Each placement
	/// gets its own session because an oversize tree terminates the one it arrived on, so running them
	/// against a shared session would only ever exercise the first.
	/// </summary>
	[TestCase(false, TestName = "An_oversize_resource_in_a_plain_tree_terminates_the_session")]
	[TestCase(true, TestName = "An_oversize_resource_in_a_fallback_subtree_terminates_the_session")]
	public async Task An_oversize_declared_resource_in_a_tree_terminates_the_session(bool inFallback)
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		var treesBefore = MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count;

		var refused = Broker.PublishSnapshot(ProviderId,
			sessionId,
			new UiRawJson(TreeWithResource(2, ProtocolLimits.MaxUiResourceBytes + 1, inFallback)));

		await WaitForAsync(() => MessagesFor<UiSessionInvalidatedEvent>("c1").Count == 1,
			"An oversize resource declaration in a tree did not terminate the session.");
		await SettleAsync();

		var lateAttach = Attach(sessionId, "c2");

		Assert.Multiple(() =>
		{
			Assert.That(refused.Accepted, Is.False);
			Assert.That(refused.Code, Is.EqualTo(UiSessionErrorCodes.PayloadTooLarge));
			Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c1"),
				Has.Count.EqualTo(treesBefore),
				"The tree carrying the oversize resource was delivered anyway.");
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1")[0].Code,
				Is.EqualTo(UiSessionErrorCodes.PayloadTooLarge));
			Assert.That(lateAttach.Code, Is.EqualTo(UiSessionErrorCodes.SessionNotFound));
		});
	}

	[Test]
	public async Task An_oversize_declared_resource_inside_a_patch_insert_is_never_delivered()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		// The provider records the resync the refusal asks for but does not answer it, so the only tree
		// this test can observe is one the host delivered on its own.
		provider.Snapshot = null;
		var treesBefore = MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count;

		var refused = Broker.PublishPatch(ProviderId,
			sessionId,
			new UiRawJson(UiPayloads.Patch(1,
				2,
				UiPayloads.InsertOperation(UiPayloads.Node("child",
						extraProperties: UiPayloads.Resource(ProtocolLimits.MaxUiResourceBytes + 1)),
					"child"))));

		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(refused.Accepted, Is.False);
			Assert.That(refused.Code, Is.EqualTo(UiSessionErrorCodes.PayloadTooLarge));
			Assert.That(MessagesFor<UiSessionPatchedEvent>("c1"), Is.Empty);
			Assert.That(MessagesFor<UiSessionTreeUpdatedEvent>("c1"), Has.Count.EqualTo(treesBefore));
		});
	}

	[Test]
	public async Task A_declared_byte_length_outside_a_node_property_bag_is_not_a_resource_declaration()
	{
		var provider = AddProvider();
		var sessionId = await OpenAsync(provider);
		Attach(sessionId, "c1");
		await WaitForMessagesAsync("c1", 1, "The client never received its first tree.");

		// A resource is an object inside a node's properties. The same two member names meeting inside a
		// surface attribute bag are ordinary provider JSON, and terminating a session over them would
		// make an arbitrary key pair fatal.
		var tree = "{\"revision\":2,\"surface\":{\"kind\":\"config\",\"sessionMode\":\"exclusive\"," +
			"\"attributes\":{\"cache\":{\"resourceId\":\"r1\",\"byteLength\":" +
			(ProtocolLimits.MaxUiResourceBytes + 1) +
			"}}}," +
			"\"root\":{\"id\":\"root\",\"type\":\"panel\",\"properties\":{},\"children\":[]}}";

		var accepted = Broker.PublishSnapshot(ProviderId, sessionId, new UiRawJson(Encoding.UTF8.GetBytes(tree)));

		await WaitForAsync(() => MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 2,
			"A byteLength outside a node's properties was treated as an oversize resource.");
		await SettleAsync();

		Assert.Multiple(() =>
		{
			Assert.That(accepted.Accepted, Is.True);
			Assert.That(MessagesFor<UiSessionInvalidatedEvent>("c1"), Is.Empty);
		});
	}

	private (int Accepted, IReadOnlyList<string?> Codes) PushPatches(string sessionId, int count, ref int revision)
	{
		var accepted = 0;
		var codes = new List<string?>();

		for (var index = 0; index < count; index++)
		{
			var result = Broker.PublishPatch(sessionId, ref revision);
			if (result.Accepted)
			{
				accepted++;
			}
			else
			{
				codes.Add(result.Code);
			}
		}

		return (accepted, codes);
	}

	private static byte[] PatchOfExactBytes(int fromRevision, int toRevision, int totalBytes)
	{
		var baseline = UiPayloads.Patch(fromRevision,
				toRevision,
				"{\"op\":\"set-properties\",\"nodeId\":\"root\",\"properties\":{\"pad\":\"\"}}")
			.Length;

		var padding = new string('a', totalBytes - baseline);

		return UiPayloads.Patch(fromRevision,
			toRevision,
			"{\"op\":\"set-properties\",\"nodeId\":\"root\",\"properties\":{\"pad\":\"" + padding + "\"}}");
	}

	private static byte[] TreeWithResource(int revision, long byteLength, bool inFallback)
	{
		var node = UiPayloads.Node("child", extraProperties: UiPayloads.Resource(byteLength));

		var root = inFallback
			? "{\"id\":\"root\",\"type\":\"panel\",\"properties\":{},\"children\":[],\"fallback\":" + node + "}"
			: "{\"id\":\"root\",\"type\":\"panel\",\"properties\":{},\"children\":[" + node + "]}";

		return UiPayloads.TreeWithRoot(revision, root);
	}
}

internal static class UiSessionLimitTestExtensions
{
	/// <summary>Publishes the next patch in the chain, advancing the caller's revision only when the host
	/// actually relayed it - a refused patch does not move the session's revision either.</summary>
	public static UiSessionIngestResult PublishPatch(this UiSessionBroker broker, string sessionId, ref int revision)
	{
		var result = broker.PublishPatch("com.example.ui",
			sessionId,
			new UiRawJson(UiPayloads.Patch(revision, revision + 1)));

		if (result.Accepted)
		{
			revision++;
		}

		return result;
	}
}
