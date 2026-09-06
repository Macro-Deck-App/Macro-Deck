using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Tests.UnitTests.Dsl;

namespace MacroDeck.Ui.Tests.UnitTests.Runtime;

/// <summary>
/// Regression coverage for the id-keyed child reconciler: what a conditional turning visible, a conditional
/// turning invisible and a reordered list each compile to. The three failure modes below are the ones nothing
/// else catches - an index computed from the authored position rather than the rendered one, a visibility change
/// answered by replacing the whole step, and a reorder answered by throwing every item away and re-inserting
/// it. All three destroy focus and in-flight edits, or make the patch inapplicable outright. Each test traces to
/// one of acceptance scenarios 13-15 for issue #540.
/// </summary>
[TestFixture]
public class UiStructuralDiffTests
{
	private const string _stepId = "setup.credentials";
	private const string _valueKey = "value";

	private static readonly string[] _itemIds = ["headers.a", "headers.b", "headers.c"];
	private static readonly string[] _reorderedItemIds = ["headers.c", "headers.a", "headers.b"];

	private static UiSurface ConfigSurface()
		=> new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };

	private static UiNode? FindById(UiNode node, string id)
	{
		if (string.Equals(node.Id, id, StringComparison.Ordinal))
		{
			return node;
		}

		foreach (var child in node.Children)
		{
			var found = FindById(child, id);
			if (found is not null)
			{
				return found;
			}
		}

		return node.Fallback is not null ? FindById(node.Fallback, id) : null;
	}

	private static IEnumerable<UiPatchOperation> AllOperations(IReadOnlyList<UiPatch> patches)
	{
		foreach (var patch in patches)
		{
			foreach (var operation in patch.Operations)
			{
				yield return operation;
			}
		}
	}

	/// <summary>
	/// A step whose authored children alternate hidden conditionals and visible inputs: conditional
	/// <c>sectionA</c>, input <c>first</c>, conditional <c>sectionB</c>, input <c>second</c>, conditional
	/// <c>sectionC</c>. With all three conditions false the rendered children are just <c>first</c> and
	/// <c>second</c>, so every authored index differs from the rendered one it would have to insert at.
	/// </summary>
	private static (UiView View, UiState<bool> GateA, UiState<bool> GateB, UiState<bool> GateC) BuildGatedStep()
	{
		var gateA = new UiState<bool>(false);
		var gateB = new UiState<bool>(false);
		var gateC = new UiState<bool>(false);

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStep
					{
						Key = "credentials",
						Children =
						[
							Gate("A", gateA),
							new UiStringInput { Key = "first" },
							Gate("B", gateB),
							new UiStringInput { Key = "second" },
							Gate("C", gateC),
						],
					},
				],
			});

		return (view, gateA, gateB, gateC);
	}

	private static UiWhen Gate(string suffix, UiState<bool> gate)
		=> new()
		{
			Key = $"gate{suffix}",
			Condition = () => gate.Value,
			Content = () => new UiAdvancedSection
			{
				Key = $"section{suffix}",
				Children = [new UiDurationInput { Key = $"timeout{suffix}" }],
			},
		};

	[Test]
	public void A_conditional_section_becoming_visible_inserts_at_its_rendered_index_not_its_authored_index()
	{
		var (view, gateA, gateB, _) = BuildGatedStep();

		view.DrainPatches();
		gateB.Value = true;

		var afterB = view.DrainPatches();

		Assert.That(afterB, Has.Count.EqualTo(1));
		Assert.That(afterB[0].Operations, Has.Count.EqualTo(1));

		var insertB = afterB[0].Operations[0];

		Assert.Multiple(() =>
		{
			Assert.That(insertB.Op, Is.EqualTo(UiPatchOperations.InsertNode));
			Assert.That(insertB.ParentId, Is.EqualTo(_stepId));
			Assert.That(insertB.Node, Is.Not.Null);
			Assert.That(insertB.NodeId, Is.EqualTo(insertB.Node!.Id));

			// The authored position is 2; the rendered one is 1, because sectionA renders nothing.
			Assert.That(insertB.Index, Is.EqualTo(1));
		});

		gateA.Value = true;

		var afterA = view.DrainPatches();

		Assert.That(afterA, Has.Count.EqualTo(1));
		Assert.That(afterA[0].Operations, Has.Count.EqualTo(1));

		var insertA = afterA[0].Operations[0];

		Assert.Multiple(() =>
		{
			Assert.That(insertA.Op, Is.EqualTo(UiPatchOperations.InsertNode));
			Assert.That(insertA.ParentId, Is.EqualTo(_stepId));
			Assert.That(insertA.Index, Is.Zero);
		});
	}

	[Test]
	public void Hiding_a_conditional_section_removes_only_that_node()
	{
		var (view, _, gateB, _) = BuildGatedStep();

		gateB.Value = true;
		view.DrainPatches();

		gateB.Value = false;

		var patches = view.DrainPatches();

		Assert.That(patches, Has.Count.EqualTo(1));
		Assert.That(patches[0].Operations, Has.Count.EqualTo(1));

		var operation = patches[0].Operations[0];

		Assert.Multiple(() =>
		{
			Assert.That(operation.Op, Is.EqualTo(UiPatchOperations.RemoveNode));
			Assert.That(operation.NodeId, Is.EqualTo("setup.credentials.sectionB"));
			Assert.That(operation.ParentId, Is.Null);
			Assert.That(operation.Node, Is.Null);

			// The section's own subtree goes with it, and nothing else is touched: a patch naming the step or a
			// surviving sibling would be throwing away their focus and in-flight edits.
			foreach (var nodeId in AllOperations(patches).Select(candidate => candidate.NodeId))
			{
				Assert.That(nodeId, Is.Not.EqualTo(_stepId));
				Assert.That(nodeId, Is.Not.EqualTo("first"));
				Assert.That(nodeId, Is.Not.EqualTo("second"));
			}

			Assert.That(FindById(view.Tree.Root, "setup.credentials.sectionB"), Is.Null);
			Assert.That(FindById(view.Tree.Root, "timeoutB"), Is.Null);
			Assert.That(FindById(view.Tree.Root, "first"), Is.Not.Null);
			Assert.That(FindById(view.Tree.Root, "second"), Is.Not.Null);
		});
	}

	[Test]
	public void A_list_reorder_emits_move_node_and_never_a_remove_insert_pair()
	{
		IReadOnlyList<HeaderItem> initial = [new("a"), new("b"), new("c")];
		var items = new UiState<IReadOnlyList<HeaderItem>>(initial);

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiArrayInput
					{
						Key = "headers",
						Children =
						[
							new UiRepeat<HeaderItem>
							{
								Key = "headerItems",
								Items = UiValue.From(() => items.Value),
								KeySelector = item => item.Id,
								Template = (item, _) => new UiStringInput
								{
									Key = item.Id,
									Binding = Bind.ReadOnly(UiValue.Of(item.Id)),
								},
							},
						],
					},
				],
			});

		var valuesBefore = ItemValues(view);

		view.DrainPatches();
		items.Value = [initial[2], initial[0], initial[1]];

		var patches = view.DrainPatches();

		Assert.That(patches, Has.Count.EqualTo(1));
		Assert.That(patches[0].Operations, Has.Count.EqualTo(1));

		var operation = patches[0].Operations[0];

		Assert.Multiple(() =>
		{
			Assert.That(operation.Op, Is.EqualTo(UiPatchOperations.MoveNode));
			Assert.That(operation.NodeId, Is.EqualTo("headers.c"));
			Assert.That(operation.ParentId, Is.EqualTo("headers"));
			Assert.That(operation.Index, Is.Zero);

			Assert.That(AllOperations(patches).Select(candidate => candidate.Op),
				Has.None.AnyOf(UiPatchOperations.InsertNode,
					UiPatchOperations.RemoveNode,
					UiPatchOperations.ReplaceNode));

			// Stable identity is the whole point of move-node: every item is still addressable by the id it
			// had, and its value survived the reorder untouched.
			Assert.That(ItemValues(view), Is.EqualTo(valuesBefore));

			var headers = FindById(view.Tree.Root, "headers")!;

			Assert.That(headers.Children.Select(child => child.Id), Is.EqualTo(_reorderedItemIds));
		});
	}

	[Test]
	public void Replacing_a_repeat_item_with_an_equal_key_and_a_new_value_updates_that_item()
	{
		IReadOnlyList<ValuedItem> initial =
			[new("a", "one"), new("b", "two"), new("c", "three")];
		var items = new UiState<IReadOnlyList<ValuedItem>>(initial);

		// A second state the item provider reads, so the repeat's scope can be invalidated while the list is
		// still the very same instance - which is the only way to observe the fast path from the outside.
		var tick = new UiState<int>(0);

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiArrayInput
					{
						Key = "headers",
						Children =
						[
							new UiRepeat<ValuedItem>
							{
								Key = "headerItems",
								Items = UiValue.From(() =>
								{
									_ = tick.Value;

									return items.Value;
								}),
								KeySelector = item => item.Id,
								Template = (item, _) => new UiStringInput
								{
									Key = item.Id,
									Binding = Bind.ReadOnly(UiValue.Of(item.Value)),
								},
							},
						],
					},
				],
			});

		view.DrainPatches();

		// Same list instance: nothing changed, so nothing is emitted and no template is re-invoked.
		tick.Value = 1;

		Assert.That(view.DrainPatches(), Is.Empty);

		// A new list with the same keys and one item's value replaced. The item's element closes over the item
		// instance, so an implementation that gates on the key sequence never updates this and the plugin's UI
		// silently shows the old value forever.
		items.Value = [initial[0], new ValuedItem("b", "TWO"), initial[2]];

		var patches = view.DrainPatches();

		Assert.That(patches, Has.Count.EqualTo(1));
		Assert.That(patches[0].Operations, Has.Count.EqualTo(1));

		var operation = patches[0].Operations[0];

		Assert.Multiple(() =>
		{
			Assert.That(operation.Op, Is.EqualTo(UiPatchOperations.SetProperties));
			Assert.That(operation.NodeId, Is.EqualTo("headers.b"));
			Assert.That(operation.Properties, Is.Not.Null);
			Assert.That(operation.Properties!, Has.Count.EqualTo(1));
			Assert.That(operation.Properties![_valueKey].GetString(), Is.EqualTo("TWO"));
			Assert.That(operation.RemovedProperties, Is.Null);

			// The keys are unchanged, so nothing is inserted, removed, moved or replaced.
			Assert.That(AllOperations(patches).Select(candidate => candidate.Op),
				Has.None.AnyOf(UiPatchOperations.InsertNode,
					UiPatchOperations.RemoveNode,
					UiPatchOperations.ReplaceNode,
					UiPatchOperations.MoveNode));

			Assert.That(FindById(view.Tree.Root, "headers.b")!.Properties[_valueKey].GetString(),
				Is.EqualTo("TWO"));
			Assert.That(FindById(view.Tree.Root, "headers.a")!.Properties[_valueKey].GetString(),
				Is.EqualTo("one"));
			Assert.That(FindById(view.Tree.Root, "headers.c")!.Properties[_valueKey].GetString(),
				Is.EqualTo("three"));
		});
	}

	/// <summary>An item whose value is carried separately from the key that identifies it.</summary>
	private sealed record ValuedItem(string Id, string Value);

	private static Dictionary<string, string?> ItemValues(UiView view)
	{
		var values = new Dictionary<string, string?>(StringComparer.Ordinal);

		foreach (var id in _itemIds)
		{
			var node = FindById(view.Tree.Root, id);

			Assert.That(node, Is.Not.Null, id);
			values[id] = node!.Properties[_valueKey].GetString();
		}

		return values;
	}
}
