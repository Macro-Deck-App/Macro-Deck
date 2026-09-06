using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Callbacks.Ui;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Ui;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;

namespace MacroDeck.Plugin.Protocol.Tests.UnitTests.Capabilities;

/// <summary>
/// The tree and the patch travel through the protocol layer as opaque JSON. A provider's own bytes are
/// what a client eventually applies, so anything the protocol layer normalises on the way in - an
/// escape, a duplicate member, insignificant whitespace, a number too long for a double - is a byte a
/// client never sees.
/// </summary>
[TestFixture]
public class UiOpaquePayloadTests
{
	// Verbatim, and every character of it load-bearing: a literal é escape, an unescaped '<', two
	// consecutive spaces before "toRevision", a duplicate "a" member, "z" declared before "a", the
	// envelope members in reverse order, an unmapped "x-vendor" member and a 39-digit integer.
	private const string AdversarialPatch =
		"{\"operations\":[{\"op\":\"set-properties\",\"nodeId\":\"field.apiKey\",\"properties\":" +
		"{\"z\":1.0,\"a\":\"caf\\u00e9 a<b\",\"a\":\"shadowed\"," +
		"\"n\":100000000000000000000000000000000000001}}],  " +
		"\"toRevision\":2,\"fromRevision\":1,\"x-vendor\":{\"keep\":true}}";

	private const string AdversarialTree =
		"{\"revision\":1,\"surface\":{\"kind\":\"config\",\"sessionMode\":\"exclusive\",\"attributes\":{}}," +
		"  \"root\":{\"id\":\"root\",\"type\":\"panel\",\"properties\":" +
		"{\"z\":1,\"a\":\"caf\\u00e9 a<b\",\"a\":\"shadowed\",\"n\":100000000000000000000000000000000000001}," +
		"\"children\":[],\"x-vendor\":{\"keep\":true}}}";

	[Test]
	public void A_ui_host_invoke_carries_the_tree_and_patch_as_opaque_json()
	{
		var patchWire = "{\"sessionId\":\"s1\",\"patch\":" + AdversarialPatch + "}";
		var treeWire = "{\"sessionId\":\"s1\",\"tree\":" + AdversarialTree + "}";

		var patchArguments = JsonSerializer.Deserialize<UiPatchArguments>(Encoding.UTF8.GetBytes(patchWire),
			PluginProtocolJson.Options);
		var snapshotArguments = JsonSerializer.Deserialize<UiSnapshotArguments>(Encoding.UTF8.GetBytes(treeWire),
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(patchArguments!.Patch.GetRawText(),
				Is.EqualTo(AdversarialPatch),
				"The patch was normalised on the way through the protocol layer.");
			Assert.That(snapshotArguments!.Tree.GetRawText(),
				Is.EqualTo(AdversarialTree),
				"The tree was normalised on the way through the protocol layer.");
		});
	}

	[Test]
	public void A_ui_host_invoke_read_from_a_whole_envelope_still_carries_the_patch_verbatim()
	{
		// The bytes a plugin in any language would put on the socket, read the way the host reads them.
		var wire = "{\"type\":\"host.invoke\",\"id\":\"01\",\"payload\":{\"api\":\"ui\",\"operation\":\"patch\"," +
			"\"arguments\":{\"sessionId\":\"s1\",\"patch\":" +
			AdversarialPatch +
			"}}}";

		var envelope = ProtocolEnvelopeReader.Read(Encoding.UTF8.GetBytes(wire)).Envelope;
		var payload = envelope!.Payload!.Value.Deserialize<HostInvokePayload>(PluginProtocolJson.Options);
		var arguments = payload!.Arguments!.Value.Deserialize<UiPatchArguments>(PluginProtocolJson.Options);

		Assert.That(arguments!.Patch.GetRawText(),
			Is.EqualTo(AdversarialPatch),
			"Reading the envelope re-encoded the patch the plugin sent.");
	}

	[Test]
	public void A_ui_capability_invoke_carries_its_provider_authored_json_as_opaque_json()
	{
		var eventWire = "{\"sessionId\":\"s1\",\"nodeId\":\"field.apiKey\",\"name\":\"input\",\"data\":" +
			"{\"value\":\"caf\\u00e9 a<b\",\"n\":100000000000000000000000000000000000001}}";
		var openWire = "{\"sessionId\":\"s1\",\"surfaceKind\":\"config\",\"sessionMode\":\"exclusive\"," +
			"\"surfaceAttributes\":{\"gridSize\":{\"w\":3,\"h\":2},\"x-vendor-unknown\":\"keep-me\"}," +
			"\"uiModelVersion\":1}";

		var eventArguments = JsonSerializer.Deserialize<UiSessionEventArguments>(Encoding.UTF8.GetBytes(eventWire),
			PluginProtocolJson.Options);
		var openArguments = JsonSerializer.Deserialize<UiSessionOpenArguments>(Encoding.UTF8.GetBytes(openWire),
			PluginProtocolJson.Options);

		Assert.Multiple(() =>
		{
			Assert.That(eventArguments!.Data!.Value.GetRawText(),
				Is.EqualTo("{\"value\":\"caf\\u00e9 a<b\",\"n\":100000000000000000000000000000000000001}"),
				"The event data was normalised on the way through the protocol layer.");
			Assert.That(openArguments!.SurfaceAttributes!.Value.GetRawText(),
				Is.EqualTo("{\"gridSize\":{\"w\":3,\"h\":2},\"x-vendor-unknown\":\"keep-me\"}"),
				"A surface attribute this host build does not recognise was dropped or rewritten.");
		});
	}

	[Test]
	public void The_ui_kind_declares_exactly_the_five_session_operations_and_no_attach()
	{
		Assert.Multiple(() =>
		{
			foreach (var operation in CapabilityOperations.Ui.All)
			{
				Assert.That(CapabilityOperations.IsKnown(CapabilityKinds.Ui, operation),
					Is.True,
					$"'{operation}' is declared but not known.");
			}

			// Attaching is a host-to-client concern; a provider is never told which clients are watching.
			Assert.That(CapabilityOperations.IsKnown(CapabilityKinds.Ui, "session.attach"), Is.False);
		});
	}

	private static readonly string[] _uiHostOperations = ["snapshot", "patch", "fault"];

	[Test]
	public void The_ui_host_api_offers_exactly_snapshot_patch_and_fault()
		=> Assert.That(HostOperations.For(HostApis.Ui), Is.EqualTo(_uiHostOperations));
}
