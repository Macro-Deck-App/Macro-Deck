using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Tests.UnitTests.Dsl;

namespace MacroDeck.Ui.Tests.UnitTests.Runtime;

/// <summary>
/// Regression coverage for the reactive runtime's central promise: a state change compiles to the smallest
/// patch that expresses it, at a cost that does not grow with the tree, and it never emits a patch a renderer
/// would have to reject. The failure mode these tests exist to catch is the one issue #540 names - a full
/// rebuild and re-serialize on every keystroke - which is unfixable once the DSL is public, plus the two ways
/// a patch stream desynchronises a renderer for good: an idle revision advance and a property removal
/// disguised as an explicit null. Each test traces to one of acceptance scenarios 8-12 and 16-18 for issue
/// #540.
/// </summary>
[TestFixture]
public class UiViewPatchTests
{
	/// <summary>The wire key an input's value is carried under. The test's own literal, so a rename in the
	/// DSL is a failure here rather than a silently agreed change.</summary>
	private const string _valueKey = "value";

	private const string _deepInputId = "deepField";

	private static readonly string[] _mixedBatchNodeIds = ["field0", "setup.advanced"];

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

	/// <summary>An optional text value: absent while the state holds null, present otherwise. Absence has to
	/// be expressed through the value itself, because a present null means "explicitly null" on the wire.
	/// </summary>
	private static UiValue<string> OptionalText(UiState<string?> state)
		=> UiValue.Optional(() => state.Value is { } text ? UiValue.Of(text) : UiValue.None<string>());

	/// <summary>
	/// Counters the <b>test</b> owns, incremented by the closures the test hands the runtime. Nothing inside
	/// MacroDeck.Ui knows they exist: the runtime's only channel for obtaining a value, a condition's result
	/// or a repeat's items is invoking one of these closures, so re-invocation is measured rather than
	/// inferred - which a rebuild-everything-then-diff implementation cannot route around.
	/// </summary>
	private sealed class ProviderProbes
	{
		private readonly Dictionary<string, int> _counts = new(StringComparer.Ordinal);

		internal void Record(string name)
			=> _counts[name] = _counts.TryGetValue(name, out var count) ? count + 1 : 1;

		internal Dictionary<string, int> Snapshot() => new(_counts, StringComparer.Ordinal);

		internal Dictionary<string, int> DeltaSince(Dictionary<string, int> snapshot)
		{
			var deltas = new Dictionary<string, int>(StringComparer.Ordinal);

			foreach (var (name, count) in _counts)
			{
				deltas[name] = count - (snapshot.TryGetValue(name, out var before) ? before : 0);
			}

			return deltas;
		}
	}

	/// <summary>What one bound-value change produced, for a tree of a given depth.</summary>
	private sealed record BoundValueChange(
		IReadOnlyList<UiPatch> Patches,
		IReadOnlyDictionary<string, int> Deltas,
		int Depth);

	/// <summary>
	/// Builds a flow of <paramref name="depth" /> nested stacks. Every level carries one instrumented input;
	/// the outermost level also carries an instrumented conditional and an instrumented repeat, so a runtime
	/// that re-evaluates structure on a property change is caught too. The deepest level carries the input
	/// bound to the returned state, and is always its parent's <b>last</b> child, which makes "the path to the
	/// changed node" navigable as "the last child, all the way down".
	/// </summary>
	private static (UiView View, ProviderProbes Probes, UiState<string> DeepState) BuildNestedStacks(int depth)
	{
		var probes = new ProviderProbes();
		var deepState = new UiState<string>("initial");
		IReadOnlyList<HeaderItem> headers = [new("h1"), new("h2")];

		UiElement innermost = new UiStringInput
		{
			Key = _deepInputId,

			// Bind.To's read/write pair with the test's counter wrapped around the read: Bind.To itself
			// cannot be counted, and what has to be measured is how often the runtime asks for this value.
			Binding = Bind.Custom(() =>
				{
					probes.Record("value:deep");

					return deepState.Value;
				},
				value => deepState.Value = value),
		};

		for (var level = depth - 1; level >= 0; level--)
		{
			var currentLevel = level;
			var children = new List<UiElement>
			{
				new UiStringInput
				{
					Key = $"field{currentLevel}",
					Binding = Bind.ReadOnly(UiValue.From(() =>
					{
						probes.Record($"value:field{currentLevel}");

						return $"v{currentLevel}";
					})),
				},
			};

			if (currentLevel == 0)
			{
				children.Add(new UiWhen
				{
					Key = "gate",
					Condition = () =>
					{
						probes.Record("condition:gate");

						return true;
					},
					Content = () =>
					{
						probes.Record("content:gate");

						return new UiStringInput
						{
							Key = "gatedField",
							Binding = Bind.ReadOnly(UiValue.From(() =>
							{
								probes.Record("value:gatedField");

								return "gated";
							})),
						};
					},
				});

				children.Add(new UiArrayInput
				{
					Key = "headers",
					Children =
					[
						new UiRepeat<HeaderItem>
						{
							Key = "headerItems",
							Items = UiValue.From(() =>
							{
								probes.Record("items:headers");

								return headers;
							}),
							KeySelector = item => item.Id,
							Template = (item, _) =>
							{
								probes.Record("template:headers");

								return new UiStringInput
								{
									Key = item.Id,
									Binding = Bind.ReadOnly(UiValue.From(() =>
									{
										probes.Record($"value:header:{item.Id}");

										return item.Id;
									})),
								};
							},
						},
					],
				});
			}

			children.Add(innermost);
			innermost = new UiConfigStack { Key = $"s{currentLevel}", Children = children };
		}

		var view = new UiView(ConfigSurface(), new UiFlow { Key = "setup", Children = [innermost] });

		return (view, probes, deepState);
	}

	private static BoundValueChange ChangeDeepValue(int depth)
	{
		var (view, probes, deepState) = BuildNestedStacks(depth);

		var beforeCounts = probes.Snapshot();
		view.DrainPatches();

		deepState.Value = "next";

		return new BoundValueChange(view.DrainPatches(), probes.DeltaSince(beforeCounts), depth);
	}

	private static void AssertOneMinimalValueOperation(BoundValueChange change)
	{
		Assert.That(change.Patches, Has.Count.EqualTo(1), $"depth {change.Depth}");

		var operations = change.Patches[0].Operations;

		Assert.Multiple(() =>
		{
			Assert.That(operations, Has.Count.EqualTo(1), $"depth {change.Depth}");
			Assert.That(operations[0].Op, Is.EqualTo(UiPatchOperations.SetProperties));
			Assert.That(operations[0].NodeId, Is.EqualTo(_deepInputId));
			Assert.That(operations[0].Properties, Is.Not.Null);
			Assert.That(operations[0].Properties!, Has.Count.EqualTo(1));
			Assert.That(operations[0].RemovedProperties, Is.Null);
			Assert.That(AllOperations(change.Patches).Select(operation => operation.Op),
				Has.None.AnyOf(UiPatchOperations.InsertNode,
					UiPatchOperations.RemoveNode,
					UiPatchOperations.ReplaceNode,
					UiPatchOperations.MoveNode));
		});

		Assert.Multiple(() =>
		{
			Assert.That(change.Deltas["value:deep"], Is.EqualTo(1), $"depth {change.Depth}");

			foreach (var (name, delta) in change.Deltas)
			{
				if (!string.Equals(name, "value:deep", StringComparison.Ordinal))
				{
					Assert.That(delta, Is.Zero, $"{name} was re-invoked at depth {change.Depth}");
				}
			}
		});
	}

	[Test]
	public void A_bound_value_change_produces_one_set_properties_operation_whose_cost_is_independent_of_tree_depth()
	{
		// Four and twelve rather than the originally scripted forty: UiCanonicalJson.MaxDepth is 32 and every
		// node costs two JSON levels, so a forty-deep tree cannot be serialized or even materialized. What
		// proves depth independence is the cross-depth equality plus the zero counter deltas, not the
		// absolute depth.
		var shallow = ChangeDeepValue(4);
		var deep = ChangeDeepValue(12);

		AssertOneMinimalValueOperation(shallow);
		AssertOneMinimalValueOperation(deep);

		Assert.Multiple(() =>
		{
			Assert.That(deep.Patches[0].Operations, Has.Count.EqualTo(shallow.Patches[0].Operations.Count));
			Assert.That(deep.Deltas.Values.Sum(), Is.EqualTo(shallow.Deltas.Values.Sum()));
		});
	}

	[Test]
	public void An_unaffected_subtree_keeps_its_node_instances_across_a_bound_value_change()
	{
		// Twelve rather than forty, for the reason given in the depth-independence test above.
		var (view, _, deepState) = BuildNestedStacks(12);

		var before = view.Tree;
		deepState.Value = "next";
		var after = view.Tree;

		var pathBefore = new List<UiNode>();
		var pathAfter = new List<UiNode>();
		var beforeNode = before.Root;
		var afterNode = after.Root;

		while (true)
		{
			pathBefore.Add(beforeNode);
			pathAfter.Add(afterNode);

			if (beforeNode.Children.Count == 0)
			{
				break;
			}

			// Every child but the last is off the path to the changed node, so it must be the very same
			// instance - reference equality, because UiNode is a record and Equals would accept a clone.
			for (var index = 0; index < beforeNode.Children.Count - 1; index++)
			{
				Assert.That(afterNode.Children[index], Is.SameAs(beforeNode.Children[index]));
			}

			beforeNode = beforeNode.Children[^1];
			afterNode = afterNode.Children[^1];
		}

		Assert.Multiple(() =>
		{
			Assert.That(pathBefore, Has.Count.EqualTo(14));
			Assert.That(pathBefore[^1].Id, Is.EqualTo(_deepInputId));

			for (var index = 0; index < pathBefore.Count; index++)
			{
				Assert.That(pathAfter[index],
					Is.Not.SameAs(pathBefore[index]),
					$"the node at depth {index} is on the path and must have been rebuilt");
			}
		});
	}

	[Test]
	public void A_state_set_to_an_equal_value_emits_no_patch_and_does_not_advance_the_revision()
	{
		var exact = new UiState<string>("abc");
		var caseInsensitive = new UiState<string>("abc", StringComparer.OrdinalIgnoreCase);

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput { Key = "exact", Binding = Bind.To(exact) },
					new UiStringInput { Key = "loose", Binding = Bind.To(caseInsensitive) },
				],
			});

		var changedCount = 0;
		view.Changed += (_, _) => changedCount++;
		var revisionBefore = view.Revision;

		exact.Value = "abc";
		caseInsensitive.Value = "ABC";

		Assert.Multiple(() =>
		{
			Assert.That(view.Revision, Is.EqualTo(revisionBefore));
			Assert.That(changedCount, Is.Zero);
			Assert.That(view.DrainPatches(), Is.Empty);
			Assert.That(caseInsensitive.Peek(), Is.EqualTo("abc"));
		});

		exact.Value = "def";

		Assert.Multiple(() =>
		{
			Assert.That(view.DrainPatches(), Has.Count.EqualTo(1));
			Assert.That(view.Revision, Is.EqualTo(revisionBefore + 1));
			Assert.That(changedCount, Is.EqualTo(1));
		});
	}

	/// <summary>The six-mutation script scenarios 11 and 12 share: a bound value, a condition turning true, a
	/// reorder, a removal, a value cleared to absent, and the condition turning false again.</summary>
	private static (UiView View, IReadOnlyList<UiPatch> Patches, int InitialRevision) RunMutationScript()
	{
		var apiKey = new UiState<string>("initial");
		var note = new UiState<string?>("abc");
		var advancedVisible = new UiState<bool>(false);
		IReadOnlyList<HeaderItem> initialHeaders = [new("a"), new("b"), new("c")];
		var headers = new UiState<IReadOnlyList<HeaderItem>>(initialHeaders);

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
							new UiStringInput { Key = "apiKey", Binding = Bind.To(apiKey) },
							new UiStringInput { Key = "note", Binding = Bind.ReadOnly(OptionalText(note)) },
							new UiArrayInput
							{
								Key = "headers",
								Children =
								[
									new UiRepeat<HeaderItem>
									{
										Key = "headerItems",
										Items = UiValue.From(() => headers.Value),
										KeySelector = item => item.Id,
										Template = (item, _) => new UiObjectInput
										{
											Key = item.Id,
											Children = [new UiStringInput { Key = "name" }],
										},
									},
								],
							},
							new UiWhen
							{
								Key = "adv",
								Condition = () => advancedVisible.Value,
								Content = () => new UiAdvancedSection
								{
									Key = "advanced",
									Children = [new UiDurationInput { Key = "timeout" }],
								},
							},
						],
					},
					new UiStep { Key = "verify" },
				],
			});

		var initialRevision = view.Revision;

		apiKey.Value = "k-1";
		advancedVisible.Value = true;
		headers.Value = [initialHeaders[2], initialHeaders[0], initialHeaders[1]];
		headers.Value = [initialHeaders[2], initialHeaders[0]];
		note.Value = null;
		advancedVisible.Value = false;

		return (view, view.DrainPatches(), initialRevision);
	}

	[Test]
	public void Every_emitted_patch_satisfies_the_sequencing_contract_and_chains_from_the_previous_one()
	{
		var (view, patches, initialRevision) = RunMutationScript();

		Assert.That(patches, Has.Count.EqualTo(6), "every mutation in the script must be observable");

		// The four structural mutations compile to their own operations rather than to a coarse replace of the
		// root; the minimality of each is scenarios 13-15's subject, but a root replace here would mean the
		// script never exercises the reconciler at all.
		Assert.That(AllOperations(patches).Where(operation =>
				string.Equals(operation.Op, UiPatchOperations.ReplaceNode, StringComparison.Ordinal)),
			Is.Empty);

		Assert.That(AllOperations(patches).Select(operation => operation.Op).Distinct(),
			Is.EquivalentTo(new[]
			{
				UiPatchOperations.SetProperties, UiPatchOperations.InsertNode, UiPatchOperations.MoveNode,
				UiPatchOperations.RemoveNode,
			}));

		Assert.Multiple(() =>
		{
			Assert.That(patches[0].FromRevision, Is.EqualTo(initialRevision));
			Assert.That(patches[^1].ToRevision, Is.EqualTo(view.Revision));

			var currentRevision = initialRevision;

			foreach (var patch in patches)
			{
				var sequencing = UiPatchSequencing.CheckRevisions(currentRevision, patch);

				Assert.That(sequencing.IsApplicable, Is.True, sequencing.RejectionReason);
				Assert.That(patch.Operations, Is.Not.Empty);

				foreach (var operation in patch.Operations)
				{
					var validation = UiPatchOperationValidation.Validate(operation);

					Assert.That(validation.IsApplicable, Is.True, validation.RejectionReason);
				}

				currentRevision = patch.ToRevision;
			}
		});
	}

	// Replaying_the_emitted_patches_onto_the_initial_tree_reproduces_the_final_tree lives in
	// MacroDeck.Ui.Testing.Tests.UnitTests: folding the patches needs UiTreeApplier, the independent oracle, which
	// sits downstream in MacroDeck.Ui.Testing. Referencing it from here would invert the layering, and writing a
	// second applier in this test would replace the very oracle the differ is being checked against.

	[Test]
	public void Clearing_a_bound_optional_value_removes_the_property_instead_of_setting_it_to_null()
	{
		var note = new UiState<string?>("abc");
		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children = [new UiStringInput { Key = "note", Binding = Bind.ReadOnly(OptionalText(note)) }],
			});

		Assert.That(FindById(view.Tree.Root, "note")!.Properties.ContainsKey(_valueKey), Is.True);

		view.DrainPatches();
		note.Value = null;

		var patches = view.DrainPatches();

		Assert.That(patches, Has.Count.EqualTo(1));
		Assert.That(patches[0].Operations, Has.Count.EqualTo(1));

		var operation = patches[0].Operations[0];
		var validation = UiPatchOperationValidation.Validate(operation);

		Assert.Multiple(() =>
		{
			Assert.That(operation.Op, Is.EqualTo(UiPatchOperations.SetProperties));
			Assert.That(operation.NodeId, Is.EqualTo("note"));
			Assert.That(operation.RemovedProperties, Is.EqualTo(new[] { _valueKey }));
			Assert.That(operation.Properties?.ContainsKey(_valueKey) ?? false, Is.False);
			Assert.That(UiCanonicalJson.Serialize(operation), Does.Not.Contain($"\"{_valueKey}\":null"));
			Assert.That(validation.IsApplicable, Is.True, validation.RejectionReason);

			// The Testing fixtures fold the patch through UiTreeApplier; the view's own tree is the same claim from
			// the producer's side - the key is gone, not present and null.
			Assert.That(FindById(view.Tree.Root, "note")!.Properties.ContainsKey(_valueKey), Is.False);
		});
	}

	[Test]
	public void An_input_whose_value_is_None_omits_the_value_property_from_the_rendered_node()
	{
		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput { Key = "explicitNone", Binding = Bind.ReadOnly(UiValue.None<string>()) },
					new UiStringInput { Key = "defaultValue", Binding = Bind.ReadOnly(default(UiValue<string>)) },
				],
			});

		var explicitNone = FindById(view.Tree.Root, "explicitNone")!;
		var defaultValue = FindById(view.Tree.Root, "defaultValue")!;

		Assert.Multiple(() =>
		{
			Assert.That(explicitNone.Properties.ContainsKey(_valueKey), Is.False);
			Assert.That(defaultValue.Properties.ContainsKey(_valueKey), Is.False);
			Assert.That(UiCanonicalJson.Serialize(explicitNone), Does.Not.Contain($"\"{_valueKey}\""));
			Assert.That(UiCanonicalJson.Serialize(defaultValue), Does.Not.Contain($"\"{_valueKey}\""));
			Assert.That(default(UiValue<string>).Equals(UiValue.None<string>()), Is.True);
		});
	}

	[Test]
	public void A_batch_collapses_every_change_inside_it_into_one_patch_and_one_revision_advance()
	{
		var states = new[]
		{
			new UiState<string>("a0"), new UiState<string>("a1"), new UiState<string>("a2"),
			new UiState<string>("a3"),
		};

		var view = BuildFourFieldView(states);
		var changedCount = 0;
		view.Changed += (_, _) => changedCount++;

		var revisionBefore = view.Revision;
		var snapshotBefore = UiCanonicalJson.Serialize(view.Tree);
		string? snapshotInsideBatch = null;

		using (view.Batch())
		{
			for (var index = 0; index < states.Length; index++)
			{
				states[index].Value = $"b{index}";
			}

			snapshotInsideBatch = UiCanonicalJson.Serialize(view.Tree);
		}

		var patches = view.DrainPatches();
		var expectedNodeIds = new[] { "field0", "field1", "field2", "field3" };

		Assert.That(patches, Has.Count.EqualTo(1));

		Assert.Multiple(() =>
		{
			Assert.That(snapshotInsideBatch, Is.EqualTo(snapshotBefore));
			Assert.That(view.Revision, Is.EqualTo(revisionBefore + 1));
			Assert.That(changedCount, Is.EqualTo(1));
			Assert.That(patches[0].FromRevision, Is.EqualTo(revisionBefore));
			Assert.That(patches[0].ToRevision, Is.EqualTo(view.Revision));
			Assert.That(patches[0].Operations.Select(operation => operation.NodeId),
				Is.EquivalentTo(expectedNodeIds));
		});

		// Control: the same four mutations outside a batch are four patches and four revisions.
		var unbatchedStates = new[]
		{
			new UiState<string>("a0"), new UiState<string>("a1"), new UiState<string>("a2"),
			new UiState<string>("a3"),
		};

		var unbatchedView = BuildFourFieldView(unbatchedStates);

		for (var index = 0; index < unbatchedStates.Length; index++)
		{
			unbatchedStates[index].Value = $"b{index}";
		}

		Assert.That(unbatchedView.DrainPatches(), Has.Count.GreaterThan(1));

		// The scenario's mixed batch: a bound value and a UiWhen toggle inside the same scope. Both changes have
		// to appear in the one patch - a batch that swallowed the structural half would expose a tree at a
		// revision the renderer was never told about.
		var gated = new UiState<bool>(false);
		var text = new UiState<string>("a0");
		var mixedView = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput { Key = "field0", Binding = Bind.To(text) },
					new UiWhen
					{
						Key = "adv",
						Condition = () => gated.Value,
						Content = () => new UiAdvancedSection
						{
							Key = "advanced",
							Children = [new UiDurationInput { Key = "timeout" }],
						},
					},
				],
			});

		var mixedChangedCount = 0;
		mixedView.Changed += (_, _) => mixedChangedCount++;
		var mixedRevisionBefore = mixedView.Revision;

		using (mixedView.Batch())
		{
			text.Value = "b0";
			gated.Value = true;
		}

		var mixedPatches = mixedView.DrainPatches();

		Assert.That(mixedPatches, Has.Count.EqualTo(1));

		var insert = mixedPatches[0].Operations.Single(operation =>
			string.Equals(operation.Op, UiPatchOperations.InsertNode, StringComparison.Ordinal));

		Assert.Multiple(() =>
		{
			Assert.That(mixedView.Revision, Is.EqualTo(mixedRevisionBefore + 1));
			Assert.That(mixedChangedCount, Is.EqualTo(1));

			// The toggle contributes its own insert-node, alongside the bound value's set-properties.
			Assert.That(insert.NodeId, Is.EqualTo("setup.advanced"));
			Assert.That(insert.ParentId, Is.EqualTo("setup"));
			Assert.That(insert.Node, Is.Not.Null);
			Assert.That(mixedPatches[0].Operations.Select(operation => operation.NodeId),
				Is.EquivalentTo(_mixedBatchNodeIds));

			Assert.That(FindById(mixedView.Tree.Root, "timeout"), Is.Not.Null);
			Assert.That(FindById(mixedView.Tree.Root, "field0")!.Properties[_valueKey].GetString(),
				Is.EqualTo("b0"));
		});
	}

	private static UiView BuildFourFieldView(UiState<string>[] states)
	{
		var children = new List<UiElement>();

		for (var index = 0; index < states.Length; index++)
		{
			children.Add(new UiStringInput { Key = $"field{index}", Binding = Bind.To(states[index]) });
		}

		return new UiView(ConfigSurface(), new UiFlow { Key = "setup", Children = children });
	}
}
