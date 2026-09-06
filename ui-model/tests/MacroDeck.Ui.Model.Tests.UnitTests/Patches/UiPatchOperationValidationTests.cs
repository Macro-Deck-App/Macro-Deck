using System.Text.Json;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;

namespace MacroDeck.Ui.Model.Tests.UnitTests.Patches;

[TestFixture]
public class UiPatchOperationValidationTests
{
	private static readonly string[] _fivePatchOperations =
		["set-properties", "insert-node", "remove-node", "replace-node", "move-node"];

	private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

	[Test]
	public void Known_patch_operations_are_exactly_the_five_frozen_names()
	{
		Assert.That(UiPatchOperations.All, Is.EqualTo(_fivePatchOperations));

		Assert.Multiple(() =>
		{
			foreach (var op in UiPatchOperations.All)
			{
				Assert.That(UiPatchOperations.IsKnown(op), Is.True);
			}

			Assert.That(UiPatchOperations.IsKnown("teleport-node"), Is.False);
			Assert.That(UiPatchOperations.IsKnown(""), Is.False);
			Assert.DoesNotThrow(() => UiPatchOperations.IsKnown(null));
			Assert.That(UiPatchOperations.IsKnown(null), Is.False);
			Assert.That(UiPatchOperations.IsKnown("Set-Properties"), Is.False);
		});
	}

	[Test]
	public void An_unknown_operation_makes_the_patch_unusable_without_throwing()
	{
		const string json = """{"fromRevision":1,"toRevision":2,"operations":[{"op":"teleport-node","nodeId":"x"}]}""";

		UiPatch? patch = null;
		Assert.DoesNotThrow(() => patch = JsonSerializer.Deserialize<UiPatch>(json, UiCanonicalJson.Options));

		var outcome = UiPatchOperationValidation.Validate(patch!.Operations[0]);

		Assert.Multiple(() =>
		{
			Assert.That(outcome.IsApplicable, Is.False);
			Assert.That(outcome.RejectionReason, Is.Not.Null.And.Not.Empty);
		});
	}

	[Test]
	public void Operation_setting_and_removing_the_same_key_is_rejected()
	{
		var conflicting = new UiPatchOperation
		{
			Op = UiPatchOperations.SetProperties,
			NodeId = "x",
			Properties = new Dictionary<string, JsonElement> { ["a"] = Element("1") },
			RemovedProperties = ["a"],
		};

		var nonConflicting = conflicting with { RemovedProperties = ["b"] };

		Assert.Multiple(() =>
		{
			Assert.That(UiPatchOperationValidation.Validate(conflicting).IsApplicable, Is.False);
			Assert.That(UiPatchOperationValidation.Validate(nonConflicting).IsApplicable, Is.True);
		});
	}

	[Test]
	public void Insert_node_requires_the_operation_id_to_match_the_carried_node()
	{
		var mismatched = new UiPatchOperation
		{
			Op = UiPatchOperations.InsertNode,
			NodeId = "x",
			ParentId = "parent",
			Node = new UiNode { Id = "y", Type = "t" },
		};

		var matched = mismatched with { Node = new UiNode { Id = "x", Type = "t" } };

		Assert.Multiple(() =>
		{
			Assert.That(UiPatchOperationValidation.Validate(mismatched).IsApplicable, Is.False);
			Assert.That(UiPatchOperationValidation.Validate(matched).IsApplicable, Is.True);
		});
	}

	[Test]
	public void A_null_removed_properties_element_is_dropped_rather_than_deserialized_as_null()
	{
		const string json = """{"op":"set-properties","nodeId":"x","removedProperties":[null]}""";

		var operation = JsonSerializer.Deserialize<UiPatchOperation>(json, UiCanonicalJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(operation.RemovedProperties, Has.Count.EqualTo(0));
			Assert.That(UiCanonicalJson.Serialize(operation), Does.Contain("\"removedProperties\":[]"));
		});
	}

	[Test]
	public void A_null_removed_properties_list_stays_null_and_is_omitted_on_write()
	{
		const string json = """{"op":"set-properties","nodeId":"x","removedProperties":null}""";

		var operation = JsonSerializer.Deserialize<UiPatchOperation>(json, UiCanonicalJson.Options)!;

		Assert.Multiple(() =>
		{
			Assert.That(operation.RemovedProperties, Is.Null);
			Assert.That(UiCanonicalJson.Serialize(operation), Does.Not.Contain("removedProperties"));
		});
	}

	[Test]
	public void Move_node_with_a_null_index_is_accepted_and_appends()
	{
		var appended = new UiPatchOperation
		{
			Op = UiPatchOperations.MoveNode,
			NodeId = "x",
			ParentId = "parent",
			Index = null,
		};

		var missingParent = appended with { ParentId = null };

		Assert.Multiple(() =>
		{
			Assert.That(UiPatchOperationValidation.Validate(appended).IsApplicable, Is.True);
			Assert.That(UiPatchOperationValidation.Validate(missingParent).IsApplicable, Is.False);
		});
	}
}
