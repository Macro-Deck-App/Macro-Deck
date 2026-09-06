using System.Reflection;
using System.Text.Json;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Ui.Model.Tests.UnitTests.Nodes;

/// <summary>
/// The acceptance criterion for the node model: unknown things are non-fatal, and null-vs-missing
/// collections behave as documented.
/// </summary>
[TestFixture]
public class UiTreeContractTests
{
	[Test]
	public void Deserializing_an_unknown_node_type_succeeds_and_preserves_it_verbatim()
	{
		const string json = """{"id":"x","type":"vendor.hologram.v2","properties":{},"children":[]}""";

		UiNode? node = null;
		Assert.DoesNotThrow(() => node = JsonSerializer.Deserialize<UiNode>(json, UiCanonicalJson.Options));
		Assert.That(node!.Type, Is.EqualTo("vendor.hologram.v2"));
	}

	[Test]
	public void Unknown_property_keys_survive_deserialization_untouched()
	{
		const string json = """
							{"id":"x","type":"t","properties":{"vendorThing":{"nested":[1,2]},"otherThing":1},"children":[]}
							""";

		var node = JsonSerializer.Deserialize<UiNode>(json, UiCanonicalJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(node.Properties, Has.Count.EqualTo(2));
			Assert.That(node.Properties["vendorThing"].GetRawText(), Is.EqualTo("{\"nested\":[1,2]}"));
		});
	}

	[Test]
	public void A_null_property_value_is_kept_as_a_null_valued_key()
	{
		const string json = """{"id":"x","type":"t","properties":{"label":null},"children":[]}""";

		var node = JsonSerializer.Deserialize<UiNode>(json, UiCanonicalJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(node.Properties.ContainsKey("label"), Is.True);
			Assert.That(node.Properties["label"].ValueKind, Is.EqualTo(JsonValueKind.Null));
		});
	}

	[Test]
	public void Unknown_members_on_a_node_are_ignored_on_read_and_absent_on_write()
	{
		const string json = """{"id":"x","type":"t","futureThing":42}""";

		var node = JsonSerializer.Deserialize<UiNode>(json, UiCanonicalJson.Options)!;

		Assert.That(UiCanonicalJson.Serialize(node),
			Is.EqualTo("""{"id":"x","type":"t","properties":{},"children":[]}"""));
	}

	[Test]
	public void Missing_collection_members_deserialize_to_empty_not_null()
	{
		const string json = """{"id":"x","type":"t"}""";

		var node = JsonSerializer.Deserialize<UiNode>(json, UiCanonicalJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(node.Properties, Is.Empty);
			Assert.That(node.Children, Is.Empty);
			Assert.That(node.RequiredComponentVersion, Is.Null);
			Assert.That(node.Fallback, Is.Null);
		});
	}

	[Test]
	public void An_explicit_null_collection_deserializes_to_empty_and_writes_as_empty()
	{
		const string json = """{"id":"x","type":"t","children":null,"properties":null}""";

		var node = JsonSerializer.Deserialize<UiNode>(json, UiCanonicalJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(node.Properties, Is.Empty);
			Assert.That(node.Children, Is.Empty);
			Assert.That(UiCanonicalJson.Serialize(node), Does.Contain("\"properties\":{}"));
			Assert.That(UiCanonicalJson.Serialize(node), Does.Contain("\"children\":[]"));
		});
	}

	[Test]
	public void A_null_child_element_is_dropped_rather_than_deserialized_as_null()
	{
		const string json = """{"id":"x","type":"t","children":[null]}""";

		var node = JsonSerializer.Deserialize<UiNode>(json, UiCanonicalJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(node.Children, Has.Count.EqualTo(0));
			Assert.That(UiCanonicalJson.Serialize(node), Does.Contain("\"children\":[]"));
		});
	}

	[Test]
	public void A_tree_round_trips_with_its_fallback_subtree_intact()
	{
		var root = new UiNode
		{
			Id = "root",
			Type = "chart",
			Fallback = new UiNode
			{
				Id = "fallback",
				Type = "text",
				Children = [new UiNode { Id = "fallback-child", Type = "text" }],
			},
		};

		var first = UiCanonicalJson.Serialize(root);
		var roundTripped = JsonSerializer.Deserialize<UiNode>(first, UiCanonicalJson.Options)!;
		var second = UiCanonicalJson.Serialize(roundTripped);

		Assert.Multiple(() =>
		{
			Assert.That(second, Is.EqualTo(first));
			Assert.That(roundTripped.Fallback!.Children[0].Id, Is.EqualTo("fallback-child"));
		});
	}

	[Test]
	public void Deeply_nested_input_fails_bounded_rather_than_crashing()
	{
		var deepJson = string.Concat(Enumerable.Repeat("{\"id\":\"x\",\"type\":\"t\",\"children\":[", 64)) +
			"{\"id\":\"leaf\",\"type\":\"t\"}" +
			string.Concat(Enumerable.Repeat("]}", 64));

		Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<UiNode>(deepJson, UiCanonicalJson.Options));
	}

	[Test]
	public void A_shared_and_an_exclusive_tree_both_construct_serialize_and_round_trip()
	{
		var shared = new UiTree
		{
			Revision = 1,
			Surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
			Root = new UiNode { Id = "root", Type = "t" },
		};
		var exclusive = shared with
		{
			Surface = new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Exclusive },
		};

		var sharedJson = UiCanonicalJson.Serialize(shared);
		var exclusiveJson = UiCanonicalJson.Serialize(exclusive);

		Assert.Multiple(() =>
		{
			Assert.That(sharedJson, Does.Contain("\"sessionMode\":\"shared\""));
			Assert.That(exclusiveJson, Does.Contain("\"sessionMode\":\"exclusive\""));
			Assert.That(sharedJson.Replace("shared", "exclusive"), Is.EqualTo(exclusiveJson));

			var sharedRoundTrip = JsonSerializer.Deserialize<UiTree>(sharedJson, UiCanonicalJson.Options)!;
			var exclusiveRoundTrip = JsonSerializer.Deserialize<UiTree>(exclusiveJson, UiCanonicalJson.Options)!;
			Assert.That(UiCanonicalJson.Serialize(sharedRoundTrip), Is.EqualTo(sharedJson));
			Assert.That(UiCanonicalJson.Serialize(exclusiveRoundTrip), Is.EqualTo(exclusiveJson));
		});
	}

	[Test]
	public void UiNode_declares_no_event_vocabulary()
	{
		var members =
			typeof(UiNode).GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);

		Assert.That(members.Any(member => member.Name.Contains("event", StringComparison.OrdinalIgnoreCase)),
			Is.False);
	}
}
