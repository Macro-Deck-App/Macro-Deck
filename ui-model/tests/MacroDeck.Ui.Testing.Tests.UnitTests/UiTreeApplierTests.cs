using System.Text.Json;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;

namespace MacroDeck.Ui.Testing.Tests.UnitTests;

/// <summary>
/// Scenarios 19 through 25 of the issue #540 acceptance fixture: the tree-dependent patch rules
/// <c>MacroDeck.Ui.Model</c> documents but does not implement - index bounds, move-node's post-removal
/// indexing, root-targeting rejections, atomicity, apply order, fallback-subtree targeting and the revision
/// gate. Every tree here is hand-written, never diff output, which is what keeps the applier an independent
/// oracle: the runtime's own patches are folded through it elsewhere, and an applier validated against the
/// differ it is meant to check would only prove the two agree.
/// </summary>
[TestFixture]
public class UiTreeApplierTests
{
	private static readonly UiSurface _surface = new()
		{ Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };

	private static readonly string[] _abc = ["a", "b", "c"];
	private static readonly string[] _bca = ["b", "c", "a"];
	private static readonly string[] _note = ["note"];

	[Test]
	public void An_index_beyond_the_child_count_rejects_and_is_never_clamped()
	{
		var root = Node("root", "flow", Node("a", "input"), Node("b", "input"));
		var tree = Tree(1, root);
		var preSerialized = UiCanonicalJson.Serialize(tree);

		var outOfBounds = Patch(1, 2, InsertNode("c", "root", Node("c", "input"), 3));

		UiApplyResult? outOfBoundsResult = null;
		Assert.DoesNotThrow(() => outOfBoundsResult = UiTreeApplier.Apply(tree, outOfBounds));

		Assert.Multiple(() =>
		{
			Assert.That(outOfBoundsResult!.IsApplied, Is.False);
			Assert.That(outOfBoundsResult.RejectionReason, Is.Not.Null.And.Not.Empty);
			Assert.That(UiCanonicalJson.Serialize(outOfBoundsResult.Tree), Is.EqualTo(preSerialized));
		});

		var atChildCount = Patch(1, 2, InsertNode("c", "root", Node("c", "input"), 2));
		var atChildCountResult = UiTreeApplier.Apply(tree, atChildCount);

		Assert.Multiple(() =>
		{
			Assert.That(atChildCountResult.IsApplied, Is.True);
			Assert.That(atChildCountResult.Tree.Root.Children.Select(n => n.Id),
				Is.EqualTo(_abc));
		});
	}

	[Test]
	public void Move_node_index_is_the_index_after_the_node_is_removed_from_its_old_position()
	{
		var root = Node("root", "flow", Node("a", "input"), Node("b", "input"), Node("c", "input"));
		var tree = Tree(1, root);
		var preSerialized = UiCanonicalJson.Serialize(tree);

		var caseI = UiTreeApplier.Apply(tree, Patch(1, 2, MoveNode("a", "root", 2)));
		var caseII = UiTreeApplier.Apply(tree, Patch(1, 2, MoveNode("a", "root", null)));
		var caseIII = UiTreeApplier.Apply(tree, Patch(1, 2, MoveNode("a", "root", 3)));

		Assert.Multiple(() =>
		{
			Assert.That(caseI.IsApplied, Is.True);
			Assert.That(caseI.Tree.Root.Children.Select(n => n.Id), Is.EqualTo(_bca));

			Assert.That(caseII.IsApplied, Is.True);
			Assert.That(caseII.Tree.Root.Children.Select(n => n.Id), Is.EqualTo(_bca));

			Assert.That(caseIII.IsApplied, Is.False);
			Assert.That(caseIII.RejectionReason, Is.Not.Null.And.Not.Empty);
			Assert.That(UiCanonicalJson.Serialize(caseIII.Tree), Is.EqualTo(preSerialized));
		});
	}

	[Test]
	public void Remove_node_and_move_node_targeting_the_root_reject_and_replace_node_on_the_root_may_not_change_its_id()
	{
		var root = Node("root", "flow", Node("a", "input"));
		var tree = Tree(1, root);
		var preSerialized = UiCanonicalJson.Serialize(tree);

		var removeRoot = UiTreeApplier.Apply(tree, Patch(1, 2, RemoveNode("root")));
		var moveRoot = UiTreeApplier.Apply(tree, Patch(1, 2, MoveNode("root", "a", null)));
		var replaceDifferentId = UiTreeApplier.Apply(tree, Patch(1, 2, ReplaceNode("root", Node("different", "flow"))));
		var replaceSameId = UiTreeApplier.Apply(tree, Patch(1, 2, ReplaceNode("root", Node("root", "stack"))));

		Assert.Multiple(() =>
		{
			Assert.That(removeRoot.IsApplied, Is.False);
			Assert.That(removeRoot.RejectionReason, Is.Not.Null.And.Not.Empty);
			Assert.That(UiCanonicalJson.Serialize(removeRoot.Tree), Is.EqualTo(preSerialized));

			Assert.That(moveRoot.IsApplied, Is.False);
			Assert.That(moveRoot.RejectionReason, Is.Not.Null.And.Not.Empty);
			Assert.That(UiCanonicalJson.Serialize(moveRoot.Tree), Is.EqualTo(preSerialized));

			Assert.That(replaceDifferentId.IsApplied, Is.False);
			Assert.That(replaceDifferentId.RejectionReason, Is.Not.Null.And.Not.Empty);
			Assert.That(UiCanonicalJson.Serialize(replaceDifferentId.Tree), Is.EqualTo(preSerialized));

			Assert.That(replaceSameId.IsApplied, Is.True);
			Assert.That(replaceSameId.Tree.Root.Id, Is.EqualTo("root"));
			Assert.That(replaceSameId.Tree.Root.Type, Is.EqualTo("stack"));
		});
	}

	[Test]
	public void A_patch_with_one_inapplicable_operation_leaves_the_tree_completely_unchanged()
	{
		var a = new UiNode
		{
			Id = "a",
			Type = "input",
			Properties = new Dictionary<string, JsonElement> { ["label"] = Element("\"original\"") },
		};

		var root = Node("root", "flow", a, Node("b", "input"));
		var tree = Tree(1, root);
		var preSerialized = UiCanonicalJson.Serialize(tree);

		var validSetProperties = new UiPatchOperation
		{
			Op = UiPatchOperations.SetProperties,
			NodeId = "a",
			Properties = new Dictionary<string, JsonElement> { ["label"] = Element("\"changed\"") },
		};

		var validInsertNode = InsertNode("c", "root", Node("c", "input"), null);

		var removeRoot = RemoveNode("root");

		var conflictingKeys = new UiPatchOperation
		{
			Op = UiPatchOperations.SetProperties,
			NodeId = "b",
			Properties = new Dictionary<string, JsonElement> { ["x"] = Element("1") },
			RemovedProperties = ["x"],
		};

		var unknownOp = new UiPatchOperation { Op = "splice-node", NodeId = "a" };

		var inapplicableThirdOperations = new[] { removeRoot, conflictingKeys, unknownOp };

		Assert.Multiple(() =>
		{
			foreach (var thirdOperation in inapplicableThirdOperations)
			{
				var patch = Patch(1, 2, validSetProperties, validInsertNode, thirdOperation);

				UiApplyResult? result = null;
				Assert.DoesNotThrow(() => result = UiTreeApplier.Apply(tree, patch));

				Assert.That(result!.IsApplied, Is.False, thirdOperation.Op);
				Assert.That(result.RejectionReason, Is.Not.Null.And.Not.Empty, thirdOperation.Op);
				Assert.That(UiCanonicalJson.Serialize(result.Tree), Is.EqualTo(preSerialized), thirdOperation.Op);
				Assert.That(result.Tree.Revision, Is.EqualTo(1), thirdOperation.Op);
			}
		});
	}

	[Test]
	public void Operations_apply_in_array_order_each_observing_the_previous_ones_effect()
	{
		var root = Node("root", "flow", Node("a", "input"));
		var tree = Tree(1, root);
		var preSerialized = UiCanonicalJson.Serialize(tree);

		var insertX = InsertNode("x", "root", Node("x", "input"), null);
		var setX = new UiPatchOperation
		{
			Op = UiPatchOperations.SetProperties,
			NodeId = "x",
			Properties = new Dictionary<string, JsonElement> { ["label"] = Element("\"hi\"") },
		};

		var forwardResult = UiTreeApplier.Apply(tree, Patch(1, 2, insertX, setX));
		var reversedResult = UiTreeApplier.Apply(tree, Patch(1, 2, setX, insertX));

		Assert.Multiple(() =>
		{
			Assert.That(forwardResult.IsApplied, Is.True);
			var x = forwardResult.Tree.Root.Children.Single(n => n.Id == "x");
			Assert.That(x.Properties["label"].GetString(), Is.EqualTo("hi"));

			Assert.That(reversedResult.IsApplied, Is.False);
			Assert.That(reversedResult.RejectionReason, Is.Not.Null.And.Not.Empty);
			Assert.That(UiCanonicalJson.Serialize(reversedResult.Tree), Is.EqualTo(preSerialized));
		});
	}

	[Test]
	public void A_patch_can_target_a_node_inside_a_fallback_subtree()
	{
		var fallback = Node("chart-fallback", "prose");
		var chart = new UiNode { Id = "chart", Type = "chart", Fallback = fallback };
		var root = Node("root", "flow", chart);
		var tree = Tree(1, root);

		var setPatch = Patch(1,
			2,
			new UiPatchOperation
			{
				Op = UiPatchOperations.SetProperties,
				NodeId = "chart-fallback",
				Properties = new Dictionary<string, JsonElement> { ["text"] = Element("\"unsupported\"") },
			});

		var setResult = UiTreeApplier.Apply(tree, setPatch);

		Assert.Multiple(() =>
		{
			Assert.That(setResult.IsApplied, Is.True);
			var updatedChart = setResult.Tree.Root.Children.Single(n => n.Id == "chart");
			Assert.That(updatedChart.Fallback, Is.Not.Null);
			Assert.That(updatedChart.Fallback!.Properties.ContainsKey("text"), Is.True);
		});

		var insertUnderFallback = Patch(1, 2, InsertNode("note", "chart-fallback", Node("note", "prose"), null));
		var insertResult = UiTreeApplier.Apply(tree, insertUnderFallback);

		Assert.Multiple(() =>
		{
			Assert.That(insertResult.IsApplied, Is.True);
			var updatedChart = insertResult.Tree.Root.Children.Single(n => n.Id == "chart");
			Assert.That(updatedChart.Fallback!.Children.Select(n => n.Id), Is.EqualTo(_note));
		});
	}

	[Test]
	public void The_applier_honours_the_revision_gate()
	{
		var root = Node("root", "flow", Node("a", "input"));
		var tree = Tree(5, root);
		var preSerialized = UiCanonicalJson.Serialize(tree);

		var validOperation = new UiPatchOperation
		{
			Op = UiPatchOperations.SetProperties,
			NodeId = "a",
			Properties = new Dictionary<string, JsonElement> { ["label"] = Element("\"x\"") },
		};

		var applicable = UiTreeApplier.Apply(tree, Patch(5, 6, validOperation));
		var wrongFromRevision = UiTreeApplier.Apply(tree, Patch(4, 6, validOperation));
		var noRevisionAdvance = UiTreeApplier.Apply(tree, Patch(5, 5, validOperation));
		var emptyOperations = UiTreeApplier.Apply(tree, Patch(5, 6));

		Assert.Multiple(() =>
		{
			Assert.That(applicable.IsApplied, Is.True);
			Assert.That(applicable.Tree.Revision, Is.EqualTo(6));

			Assert.That(wrongFromRevision.IsApplied, Is.False);
			Assert.That(UiCanonicalJson.Serialize(wrongFromRevision.Tree), Is.EqualTo(preSerialized));

			Assert.That(noRevisionAdvance.IsApplied, Is.False);
			Assert.That(UiCanonicalJson.Serialize(noRevisionAdvance.Tree), Is.EqualTo(preSerialized));

			Assert.That(emptyOperations.IsApplied, Is.False);
			Assert.That(UiCanonicalJson.Serialize(emptyOperations.Tree), Is.EqualTo(preSerialized));
		});
	}

	private static UiNode Node(string id, string type, params UiNode[] children)
		=> new() { Id = id, Type = type, Children = children };

	private static UiTree Tree(int revision, UiNode root) =>
		new() { Revision = revision, Surface = _surface, Root = root };

	private static JsonElement Element(string json) => JsonDocument.Parse(json).RootElement.Clone();

	private static UiPatch Patch(int fromRevision, int toRevision, params UiPatchOperation[] operations)
		=> new() { FromRevision = fromRevision, ToRevision = toRevision, Operations = operations };

	private static UiPatchOperation InsertNode(string nodeId, string parentId, UiNode node, int? index)
		=> new()
		{
			Op = UiPatchOperations.InsertNode, NodeId = nodeId, ParentId = parentId, Node = node, Index = index
		};

	private static UiPatchOperation RemoveNode(string nodeId) =>
		new() { Op = UiPatchOperations.RemoveNode, NodeId = nodeId };

	private static UiPatchOperation ReplaceNode(string nodeId, UiNode node)
		=> new() { Op = UiPatchOperations.ReplaceNode, NodeId = nodeId, Node = node };

	private static UiPatchOperation MoveNode(string nodeId, string parentId, int? index)
		=> new() { Op = UiPatchOperations.MoveNode, NodeId = nodeId, ParentId = parentId, Index = index };
}
