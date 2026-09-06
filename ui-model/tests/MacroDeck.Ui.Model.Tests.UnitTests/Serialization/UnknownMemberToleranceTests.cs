using System.Text.Json;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Negotiation;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Ui.Model.Tests.UnitTests.Serialization;

/// <summary>
/// An injected <c>"unknownField"</c> on every wire record deserializes fine and is dropped on
/// re-serialize - the non-fatality acceptance criterion, proven per type rather than asserted once for
/// <see cref="UiNode" /> alone.
/// </summary>
[TestFixture]
public class UnknownMemberToleranceTests
{
	[Test]
	public void An_unknown_member_on_a_node_is_dropped_on_reserialize()
		=> AssertDropped<UiNode>("""{"id":"x","type":"t","unknownField":42}""", "id", "type");

	[Test]
	public void An_unknown_member_on_a_tree_is_dropped_on_reserialize()
	{
		const string json = """
							{"revision":1,"surface":{"kind":"config","sessionMode":"shared"},"root":{"id":"x","type":"t"},
							"unknownField":42}
							""";
		AssertDropped<UiTree>(json, "revision", "surface", "root");
	}

	[Test]
	public void An_unknown_member_on_a_surface_is_dropped_on_reserialize()
		=> AssertDropped<UiSurface>("""{"kind":"config","sessionMode":"shared","unknownField":42}""",
			"kind",
			"sessionMode");

	[Test]
	public void An_unknown_member_on_an_event_is_dropped_on_reserialize()
		=> AssertDropped<UiEvent>("""{"nodeId":"x","name":"click","unknownField":42}""", "nodeId", "name");

	[Test]
	public void An_unknown_member_on_a_patch_is_dropped_on_reserialize()
		=> AssertDropped<UiPatch>(
			"""{"fromRevision":1,"toRevision":2,"operations":[{"op":"remove-node","nodeId":"x"}],"unknownField":42}""",
			"fromRevision",
			"toRevision",
			"operations");

	[Test]
	public void An_unknown_member_on_a_patch_operation_is_dropped_on_reserialize()
		=> AssertDropped<UiPatchOperation>("""{"op":"remove-node","nodeId":"x","unknownField":42}""", "op", "nodeId");

	[Test]
	public void An_unknown_member_on_a_resource_is_dropped_on_reserialize()
		=> AssertDropped<UiResource>("""{"resourceId":"icon-1","unknownField":42}""", "resourceId");

	[Test]
	public void An_unknown_member_on_capabilities_is_dropped_on_reserialize()
		=> AssertDropped<UiCapabilities>(
			"""{"uiProtocol":{"minimum":1,"maximum":1},"supportsAllComponents":true,"unknownField":42}""",
			"uiProtocol",
			"supportsAllComponents");

	[Test]
	public void An_unknown_member_on_a_version_range_is_dropped_on_reserialize()
		=> AssertDropped<UiVersionRange>("""{"minimum":1,"maximum":2,"unknownField":42}""", "minimum", "maximum");

	private static void AssertDropped<T>(string jsonWithUnknownMember, params string[] expectedKeys)
	{
		T? value = default;
		Assert.DoesNotThrow(() =>
			value = JsonSerializer.Deserialize<T>(jsonWithUnknownMember, UiCanonicalJson.Options));

		var reserialized = UiCanonicalJson.Serialize(value);

		Assert.Multiple(() =>
		{
			Assert.That(reserialized, Does.Not.Contain("unknownField"));
			foreach (var key in expectedKeys)
			{
				Assert.That(reserialized, Does.Contain($"\"{key}\""));
			}
		});
	}
}
