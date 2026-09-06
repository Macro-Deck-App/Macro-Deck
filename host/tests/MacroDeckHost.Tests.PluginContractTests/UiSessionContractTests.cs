using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeckHost.Application.Ui.Transport.Messages.UiSessions;
using MacroDeckHost.Tests.PluginContractTests.Harness;

namespace MacroDeckHost.Tests.PluginContractTests;

/// <summary>
/// An out-of-process provider's bytes, end to end. Everything here is written as the bytes a plugin
/// process puts on the socket rather than as an object the host serialises for it, because the whole
/// criterion is that nothing between the provider and the client re-encodes the payload.
/// </summary>
[TestFixture]
internal sealed class UiSessionContractTests : UiContractFixture
{
	// Verbatim. Every character is load-bearing - see the assertions, each of which names the transform
	// it would catch.
	private const string AdversarialPatch =
		"{\"operations\":[{\"op\":\"set-properties\",\"nodeId\":\"field.apiKey\",\"properties\":" +
		"{\"z\":1.0,\"a\":\"caf\\u00e9 a<b\",\"a\":\"shadowed\"," +
		"\"n\":100000000000000000000000000000000000001}}],  " +
		"\"toRevision\":2,\"fromRevision\":1,\"x-vendor\":{\"keep\":true}}";

	// Same construction as the patch, on a tree: "root" declared before "surface" against the model's
	// own property order, two spaces before it, unsorted and duplicated property keys, an escaped
	// non-ASCII string, a fallback subtree and an unmapped member on the root node.
	private const string AdversarialTree =
		"{\"revision\":1,  \"root\":{\"id\":\"root\",\"type\":\"panel\",\"properties\":" +
		"{\"z\":1.0,\"a\":\"caf\\u00e9 a<b\",\"a\":\"shadowed\"," +
		"\"n\":100000000000000000000000000000000000001}," +
		"\"children\":[],\"fallback\":{\"id\":\"fb\",\"type\":\"text\",\"properties\":{},\"children\":[]}," +
		"\"x-vendor\":{\"keep\":true}}," +
		"\"surface\":{\"kind\":\"config\",\"sessionMode\":\"exclusive\",\"attributes\":{}}}";

	private const string PlainTree =
		"{\"revision\":1,\"surface\":{\"kind\":\"config\",\"sessionMode\":\"exclusive\",\"attributes\":{}}," +
		"\"root\":{\"id\":\"root\",\"type\":\"panel\",\"properties\":{},\"children\":[]}}";

	private TestUiCapabilityHandler _handler = null!;

	private static DeclaredCapability Provider()
		=> new()
		{
			Kind = CapabilityKinds.Ui,
			LocalId = ProviderCapabilityId.LocalId,
			VersionRange = new CapabilityVersionRange { Minimum = 1, Maximum = 1 }
		};

	private async Task<string> ConnectAndOpenAsync(string treeJson,
		IReadOnlyDictionary<string, JsonElement>? attributes = null)
	{
		_handler = new TestUiCapabilityHandler
		{
			OnSnapshotRequested = sessionId => Link.SendRawFromPluginAsync(UiWire.Snapshot(sessionId, treeJson))
		};

		await ConnectAsync([_handler], [Provider()], [CapabilityKinds.Ui]);

		var surface = new UiSurface
		{
			Kind = UiSurfaceKinds.Config,
			SessionMode = UiSessionModes.Exclusive,
			Attributes = attributes ?? new Dictionary<string, JsonElement>(StringComparer.Ordinal)
		};

		var ticket = await Broker.OpenAsync(PluginId, surface, OwnerPrincipal, CancellationToken.None);
		Assert.That(ticket.Accepted, Is.True, "The plugin-served session did not open.");
		return ticket.SessionId;
	}

	[Test]
	public async Task An_out_of_process_provider_receives_session_open_with_the_surface_preserved_verbatim()
	{
		var attributes = JsonSerializer
			.Deserialize<Dictionary<string, JsonElement>>(
				"{\"gridSize\":{\"w\":3,\"h\":2},\"x-vendor-unknown\":\"keep-me\"}",
				PluginProtocolJson.Options)!;

		await ConnectAndOpenAsync(PlainTree, attributes);

		var opens = SessionOpenInvocations();

		Assert.That(opens, Has.Count.EqualTo(1), "session.open was sent more or less than once.");

		var arguments = opens[0];
		var surfaceAttributes = arguments.GetProperty("surfaceAttributes");

		Assert.Multiple(() =>
		{
			Assert.That(arguments.GetProperty("surfaceKind").GetString(), Is.EqualTo(UiSurfaceKinds.Config));
			Assert.That(arguments.GetProperty("sessionMode").GetString(), Is.EqualTo(UiSessionModes.Exclusive));
			Assert.That(surfaceAttributes.GetProperty("gridSize").GetProperty("w").GetInt32(), Is.EqualTo(3));
			Assert.That(surfaceAttributes.GetProperty("gridSize").GetProperty("h").GetInt32(), Is.EqualTo(2));
			Assert.That(surfaceAttributes.GetProperty("x-vendor-unknown").GetString(),
				Is.EqualTo("keep-me"),
				"A surface attribute this host build does not recognise was dropped on the way to the provider.");
			Assert.That(_handler.Opened.Single().SessionId, Is.Not.Empty);
		});
	}

	[Test]
	public async Task Session_open_carries_a_surface_attributes_member_even_when_the_surface_declares_none()
	{
		await ConnectAndOpenAsync(PlainTree);

		var arguments = SessionOpenInvocations().Single();

		Assert.That(arguments.TryGetProperty("surfaceAttributes", out var attributes),
			Is.True,
			"An empty attribute map was omitted rather than written.");
		Assert.That(attributes.ValueKind, Is.EqualTo(JsonValueKind.Object));
	}

	[Test]
	public async Task A_forwarded_patch_reaches_the_client_as_the_exact_bytes_the_provider_produced()
	{
		var sessionId = await ConnectAndOpenAsync(PlainTree);
		Attach(sessionId, "c1");
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received its tree.");

		await Link.SendRawFromPluginAsync(UiWire.Patch(sessionId, AdversarialPatch));
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionPatchedEvent>("c1").Count == 1,
			"The provider's patch never reached the client.");

		var pushed = UiTransport.MessagesFor<UiSessionPatchedEvent>("c1").Single();

		AssertVerbatim(AdversarialPatch, pushed.Patch.Utf8.ToArray(), "toRevision", "fromRevision");

		// The relay is only half the path. The client sees the bytes the hub's JSON protocol writes, so
		// the same payload has to survive that hop too.
		UiWebSocketEgress.AssertCarriesVerbatim(pushed,
			Encoding.UTF8.GetBytes(AdversarialPatch),
			"A patch reaches the client as the exact bytes the provider produced.");
	}

	[Test]
	public async Task A_forwarded_snapshot_tree_reaches_the_client_as_the_exact_bytes_the_provider_produced()
	{
		var sessionId = await ConnectAndOpenAsync(AdversarialTree);
		Attach(sessionId, "c1");
		await WaitForUiAsync(() => UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Count == 1,
			"The client never received its tree.");

		var pushed = UiTransport.MessagesFor<UiSessionTreeUpdatedEvent>("c1").Single();

		AssertVerbatim(AdversarialTree, pushed.Tree.Utf8.ToArray(), "root", "surface");

		UiWebSocketEgress.AssertCarriesVerbatim(pushed,
			Encoding.UTF8.GetBytes(AdversarialTree),
			"A tree reaches the client as the exact bytes the provider produced.");
	}

	/// <summary>Full byte equality plus one assertion per transform, so a failure says which round trip
	/// crept in rather than only that one did.</summary>
	private static void AssertVerbatim(string expected, byte[] delivered, string laterMember, string earlierMember)
	{
		var text = Encoding.UTF8.GetString(delivered);

		Assert.Multiple(() =>
		{
			Assert.That(delivered, Is.EqualTo(Encoding.UTF8.GetBytes(expected)));

			Assert.That(text,
				Does.Contain("caf\\u00e9"),
				"The literal escape was unescaped: a reader/writer pass emitted the UTF-8 bytes of e-acute.");
			Assert.That(text,
				Does.Contain("a<b"),
				"'<' was re-escaped: JsonElement.WriteTo emits a\\u003Cb.");
			Assert.That(text,
				Does.Contain("  \"" + laterMember + "\""),
				"Insignificant whitespace was collapsed: any reader/writer pass drops it.");
			Assert.That(text.Split("\"a\":").Length - 1,
				Is.EqualTo(2),
				"A duplicate member was collapsed: binding to a dictionary keeps only the last.");
			Assert.That(text.IndexOf("\"z\":", StringComparison.Ordinal),
				Is.LessThan(text.IndexOf("\"a\":", StringComparison.Ordinal)),
				"Map keys were sorted: UiCanonicalJson orders them ordinal ascending.");
			Assert.That(text.IndexOf("\"" + laterMember + "\"", StringComparison.Ordinal),
				Is.LessThan(text.IndexOf("\"" + earlierMember + "\"", StringComparison.Ordinal)),
				"Envelope members were reordered: re-serializing the model restores [JsonPropertyOrder].");
			Assert.That(text,
				Does.Contain("\"x-vendor\""),
				"An unmapped member was dropped: UnmappedMemberHandling.Skip discards it.");
			Assert.That(text,
				Does.Contain("100000000000000000000000000000000000001"),
				"A 39-digit integer lost digits: a double hop collapses it.");
		});
	}

	private IReadOnlyList<JsonElement> SessionOpenInvocations()
		=>
		[
			.. Link.SentByHost
				.Where(envelope =>
					string.Equals(envelope.Type, MessageTypes.CapabilityInvoke, StringComparison.Ordinal))
				.Select(envelope =>
					envelope.Payload!.Value.Deserialize<CapabilityInvokePayload>(PluginProtocolJson.Options)!)
				.Where(payload => string.Equals(payload.Kind, CapabilityKinds.Ui, StringComparison.Ordinal) &&
					string.Equals(payload.Operation, CapabilityOperations.Ui.SessionOpen, StringComparison.Ordinal))
				.Select(payload => payload.Arguments!.Value)
		];
}
