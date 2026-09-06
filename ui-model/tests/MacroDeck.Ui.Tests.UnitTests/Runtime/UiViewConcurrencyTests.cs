using System.Collections.Concurrent;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Serialization;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using static MacroDeck.Ui.Tests.UnitTests.Runtime.UiConcurrency;

namespace MacroDeck.Ui.Tests.UnitTests.Runtime;

/// <summary>
/// Regression coverage for issue #830: the runtime aborted the whole host with
/// <c>Collection was modified; enumeration operation may not execute</c> when the variable-history timer
/// thread wrote state while a socket pump was inside <c>Dispatch</c> on the same view. The contract these
/// tests hold to is the one that replaced it - any thread may read, write, dispatch and drain, a write is
/// atomic against a concurrent dispatch on the same view, and views sharing no state proceed in parallel.
///
/// <para>
/// Concurrency here only builds the state; every verdict is taken single-threaded once every worker has
/// joined, which is what keeps a correct implementation from flaking. Every wait is bounded and asserted, so
/// an implementation that deadlocks fails an assertion instead of hanging CI, and every worker's exceptions
/// are funnelled into a bag the test thread asserts empty - a fault on a pool thread would otherwise vanish
/// and leave the test passing vacuously.
/// </para>
/// </summary>
[TestFixture]
public class UiViewConcurrencyTests
{
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
			if (FindById(child, id) is { } found)
			{
				return found;
			}
		}

		return node.Fallback is not null ? FindById(node.Fallback, id) : null;
	}

	private static double NumberOf(UiView view, string nodeId)
		=> FindById(view.Tree.Root, nodeId)!.Properties[UiConfigProperties.Value].GetDouble();

	private static double NumberOf(UiTree tree, string nodeId)
		=> FindById(tree.Root, nodeId)!.Properties[UiConfigProperties.Value].GetDouble();

	private static IEnumerable<UiPatchOperation> AllOperations(IEnumerable<UiPatch> patches)
		=> patches.SelectMany(patch => patch.Operations);

	/// <summary>A read-only number node whose id is its key, so an assertion can name the node the state
	/// feeds without knowing anything about how the runtime composes ids.</summary>
	private static UiStringInput TextNode(string key, Func<string> read)
		=> new() { Key = key, Binding = Bind.ReadOnly(UiValue.From(read)) };

	private static UiNumberInput NumberNode(string key, Func<double> read)
		=> new() { Key = key, Binding = Bind.ReadOnly(UiValue.From(read)) };

	private static UiEvent ChangeEvent(string nodeId, string payload)
		=> new() { NodeId = nodeId, Name = UiConfigEvents.Change, Data = UiCanonicalJson.ToElement(payload) };

	[Test]
	[CancelAfter(30_000)]
	public void Concurrent_writers_and_a_concurrent_dispatcher_never_fault_the_runtime()
	{
		// The dependency sets have to genuinely churn for the reported ClearDependencies crash to be
		// reachable: the provider reads a different state depending on the toggle, and the UiWhen on the same
		// toggle re-materializes the subtree underneath it while that is happening.
		const int iterations = 5000;

		var toggle = new UiState<bool>(true);
		var left = new UiState<double>(0);
		var right = new UiState<double>(0);
		var spare = new UiState<double>(0);
		var failures = new ConcurrentBag<Exception>();

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					NumberNode("switched", () => toggle.Value ? left.Value : right.Value),
					NumberNode("spare", () => spare.Value),
					new UiWhen
					{
						Key = "gate",
						Condition = () => toggle.Value,
						Content = () => NumberNode("gated", () => left.Value),
					},
					new UiStringInput
					{
						Key = "dispatchField",
						Binding = Bind.ReadOnly(UiValue.From(() => "typed")),
						Events = [UiEventHandler.On(UiConfigEvents.Change, () => { })],
					},
				],
			});

		RunConcurrently(failures,
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					toggle.Value = index % 2 == 0;
				}
			},
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					left.Value = index;
				}
			},
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					right.Value = index;
				}
			},
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					spare.Value = index;
				}
			},
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					var result = view.Dispatch(ChangeEvent("dispatchField", "typed"));

					if (!result.IsAccepted)
					{
						throw new InvalidOperationException($"the dispatcher was refused: {result.Reason}");
					}
				}
			});

		AssertNoWorkerFaulted(failures);

		Assert.That(view.Revision, Is.GreaterThanOrEqualTo(1));

		// The runtime has to be left working, not merely un-crashed: a value no writer ever wrote still has to
		// compile to a patch.
		toggle.Value = true;
		view.DrainPatches();
		left.Value = -1;

		Assert.That(view.DrainPatches(), Is.Not.Empty, "the view stopped emitting patches after the storm");
	}

	[Test]
	[CancelAfter(30_000)]
	public void Every_state_written_concurrently_converges_on_the_last_value_written_to_it()
	{
		const int writers = 8;
		const int last = 2000;

		var states = new UiState<double>[writers];
		var children = new List<UiElement>();

		for (var index = 0; index < writers; index++)
		{
			var state = new UiState<double>(0);
			states[index] = state;
			children.Add(NumberNode($"node{index}", () => state.Value));
		}

		var failures = new ConcurrentBag<Exception>();
		var view = new UiView(ConfigSurface(), new UiFlow { Key = "setup", Children = children });

		var bodies = new Action[writers];

		for (var index = 0; index < writers; index++)
		{
			var state = states[index];

			bodies[index] = () =>
			{
				for (var value = 1; value <= last; value++)
				{
					state.Value = value;
				}
			};
		}

		RunConcurrently(failures, bodies);

		AssertNoWorkerFaulted(failures);

		Assert.Multiple(() =>
		{
			for (var index = 0; index < writers; index++)
			{
				Assert.That(NumberOf(view, $"node{index}"), Is.EqualTo((double)last), $"node{index}");
			}
		});

		// The permanent-damage detector: a runtime that dropped a dependent or left a cell wedged converges
		// above and then never patches that node again.
		view.DrainPatches();

		var patches = new List<UiPatch>();

		for (var index = 0; index < writers; index++)
		{
			states[index].Value = last + 1;
			patches.AddRange(view.DrainPatches());
		}

		Assert.That(patches, Has.Count.EqualTo(writers));

		var named = new List<string>();

		Assert.Multiple(() =>
		{
			foreach (var patch in patches)
			{
				Assert.That(patch.Operations, Has.Count.EqualTo(1));
				Assert.That(patch.Operations[0].Op, Is.EqualTo(UiPatchOperations.SetProperties));
				Assert.That(patch.Operations[0].Properties![UiConfigProperties.Value].GetDouble(),
					Is.EqualTo((double)(last + 1)));

				named.Add(patch.Operations[0].NodeId!);
			}
		});

		Assert.That(named,
			Is.EquivalentTo(Enumerable.Range(0, writers).Select(index => $"node{index}")));
	}

	[Test]
	[CancelAfter(30_000)]
	public void The_patch_stream_stays_applicable_however_concurrent_writes_interleaved()
	{
		const int writers = 8;
		const int last = 1000;

		var states = new UiState<double>[writers];
		var children = new List<UiElement>();

		for (var index = 0; index < writers; index++)
		{
			var state = new UiState<double>(0);
			states[index] = state;
			children.Add(NumberNode($"node{index}", () => state.Value));
		}

		var failures = new ConcurrentBag<Exception>();
		var view = new UiView(ConfigSurface(), new UiFlow { Key = "setup", Children = children });
		var initial = view.Tree;

		Assert.That(initial.Revision,
			Is.Zero,
			"the fixture has to start at revision 0 for the replay to mean anything");

		var bodies = new Action[writers];

		for (var index = 0; index < writers; index++)
		{
			var state = states[index];

			bodies[index] = () =>
			{
				for (var value = 1; value <= last; value++)
				{
					state.Value = value;
				}
			};
		}

		RunConcurrently(failures, bodies);

		AssertNoWorkerFaulted(failures);

		var patches = view.DrainPatches();
		var replayed = initial;

		Assert.That(patches, Is.Not.Empty);
		Assert.That(patches[0].FromRevision, Is.Zero);

		Assert.Multiple(() =>
		{
			for (var index = 0; index < patches.Count; index++)
			{
				var patch = patches[index];

				Assert.That(patch.Operations, Is.Not.Empty, $"patch {index} carried no operations");
				Assert.That(patch.ToRevision, Is.EqualTo(patch.FromRevision + 1), $"patch {index}");

				if (index > 0)
				{
					Assert.That(patch.FromRevision, Is.EqualTo(patches[index - 1].ToRevision), $"patch {index}");
				}
			}
		});

		for (var index = 0; index < patches.Count; index++)
		{
			var result = UiTreeApplier.Apply(replayed, patches[index]);

			Assert.That(result.IsApplied, Is.True, $"patch {index} did not apply: {result.RejectionReason}");

			replayed = result.Tree;
		}

		Assert.Multiple(() =>
		{
			Assert.That(replayed.Revision, Is.EqualTo(view.Revision));
			Assert.That(UiCanonicalJson.Serialize(replayed), Is.EqualTo(UiCanonicalJson.Serialize(view.Tree)));

			// Emitting nothing at all would satisfy every rule above, so the replay has to have moved.
			Assert.That(UiCanonicalJson.Serialize(replayed), Is.Not.EqualTo(UiCanonicalJson.Serialize(initial)));

			for (var index = 0; index < writers; index++)
			{
				Assert.That(NumberOf(replayed, $"node{index}"), Is.EqualTo((double)last), $"node{index}");
			}
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public void Draining_from_another_thread_never_eats_a_patch()
	{
		const int writes = 500;

		var state = new UiState<double>(0);
		var failures = new ConcurrentBag<Exception>();
		var view = new UiView(ConfigSurface(),
			new UiFlow { Key = "setup", Children = [NumberNode("counter", () => state.Value)] });

		var drained = new List<UiPatch>();
		var writerDone = false;

		RunConcurrently(failures,
			() =>
			{
				for (var value = 1; value <= writes; value++)
				{
					state.Value = value;
				}

				Volatile.Write(ref writerDone, true);
			},
			() =>
			{
				while (!Volatile.Read(ref writerDone))
				{
					drained.AddRange(view.DrainPatches());
				}

				drained.AddRange(view.DrainPatches());
			});

		AssertNoWorkerFaulted(failures);

		drained.AddRange(view.DrainPatches());

		Assert.That(drained, Has.Count.EqualTo(writes));

		Assert.Multiple(() =>
		{
			for (var index = 0; index < drained.Count; index++)
			{
				var patch = drained[index];

				Assert.That(patch.FromRevision, Is.EqualTo(index), $"patch {index}");
				Assert.That(patch.ToRevision, Is.EqualTo(index + 1), $"patch {index}");
				Assert.That(patch.Operations, Has.Count.EqualTo(1), $"patch {index}");
				Assert.That(patch.Operations[0].Op, Is.EqualTo(UiPatchOperations.SetProperties), $"patch {index}");
				Assert.That(patch.Operations[0].NodeId, Is.EqualTo("counter"), $"patch {index}");
				Assert.That(patch.Operations[0].Properties![UiConfigProperties.Value].GetDouble(),
					Is.EqualTo((double)(index + 1)),
					$"patch {index}");
			}
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public void A_Changed_handler_may_read_drain_and_write_on_the_thread_that_performed_the_write()
	{
		var outer = new UiState<double>(0);
		var inner = new UiState<double>(0);
		var failures = new ConcurrentBag<Exception>();

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children = [NumberNode("outer", () => outer.Value), NumberNode("inner", () => inner.Value)],
			});

		view.DrainPatches();

		var reentered = false;
		var handlerThreadId = 0;
		var sawTree = false;

		view.Changed += (_, _) =>
		{
			if (reentered)
			{
				return;
			}

			reentered = true;
			handlerThreadId = Environment.CurrentManagedThreadId;
			sawTree = view.Tree.Revision >= 1;
			view.DrainPatches();
			inner.Value = 7;
		};

		var writerThreadId = 0;

		RunConcurrently(failures,
			() =>
			{
				writerThreadId = Environment.CurrentManagedThreadId;
				outer.Value = 1;
			});

		AssertNoWorkerFaulted(failures);

		Assert.Multiple(() =>
		{
			Assert.That(reentered, Is.True, "the Changed handler never ran");
			Assert.That(sawTree, Is.True, "the handler could not read the tree the patch already covered");

			// Pins that Changed is raised on the thread that did the work rather than marshalled onto a pump.
			Assert.That(handlerThreadId, Is.EqualTo(writerThreadId));
			Assert.That(NumberOf(view, "inner"), Is.EqualTo(7d));
			Assert.That(AllOperations(view.DrainPatches()).Select(operation => operation.NodeId),
				Does.Contain("inner"),
				"the write made from inside the handler produced no patch of its own");
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public void A_faulting_handler_still_rejects_and_still_reports_while_another_thread_writes()
	{
		const int dispatches = 200;
		const string declined = "not filled in yet";

		var faulted = new UiState<double>(0);
		var declining = new UiState<double>(0);
		var unrelated = new UiState<double>(0);
		var failures = new ConcurrentBag<Exception>();

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					NumberNode("faultedValue", () => faulted.Value),
					NumberNode("declinedValue", () => declining.Value),
					NumberNode("unrelatedValue", () => unrelated.Value),
					new UiStringInput
					{
						Key = "thrower",
						Binding = Bind.ReadOnly(UiValue.From(() => "thrower")),
						Events =
						[
							UiEventHandler.On(UiConfigEvents.Change,
								data =>
								{
									faulted.Value = data.TryGetString(out var text)
										? double.Parse(text,
											System.Globalization.CultureInfo.InvariantCulture)
										: -1;

									throw new InvalidOperationException("boom");
								}),
						],
					},
					new UiStringInput
					{
						Key = "decliner",
						Binding = Bind.ReadOnly(UiValue.From(() => "decliner")),
						Events =
						[
							UiEventHandler.On(UiConfigEvents.Change,
								data =>
								{
									declining.Value = data.TryGetString(out var text)
										? double.Parse(text,
											System.Globalization.CultureInfo.InvariantCulture)
										: -1;

									return UiEventOutcome.Rejected(declined);
								}),
						],
					},
				],
			});

		var initial = view.Tree;
		var faults = 0;
		var faultedNodes = new ConcurrentBag<string>();

		view.HandlerFaulted += (_, args) =>
		{
			Interlocked.Increment(ref faults);
			faultedNodes.Add(args.NodeId);
		};

		var results = new UiDispatchResult[dispatches];
		var lastThrown = 0d;
		var lastDeclined = 0d;

		RunConcurrently(failures,
			() =>
			{
				for (var index = 0; index < dispatches; index++)
				{
					var value = index + 1;
					var payload = value.ToString(System.Globalization.CultureInfo.InvariantCulture);

					if (index % 2 == 0)
					{
						results[index] = view.Dispatch(ChangeEvent("thrower", payload));
						lastThrown = value;
					}
					else
					{
						results[index] = view.Dispatch(ChangeEvent("decliner", payload));
						lastDeclined = value;
					}
				}
			},
			() =>
			{
				for (var value = 1; value <= dispatches; value++)
				{
					unrelated.Value = value;
				}
			});

		AssertNoWorkerFaulted(failures);

		Assert.Multiple(() =>
		{
			for (var index = 0; index < dispatches; index++)
			{
				Assert.That(results[index].Outcome, Is.EqualTo(UiDispatchOutcome.Rejected), $"dispatch {index}");

				if (index % 2 == 0)
				{
					Assert.That(results[index].Reason,
						Does.Contain("thrower").And.Contain("boom"),
						$"dispatch {index}");
				}
				else
				{
					Assert.That(results[index].Reason, Is.EqualTo(declined), $"dispatch {index}");
				}
			}
		});

		Assert.Multiple(() =>
		{
			Assert.That(faults, Is.EqualTo(dispatches / 2), "a faulting handler is reported exactly once per fault");
			Assert.That(faultedNodes, Has.All.EqualTo("thrower"), "declining is not faulting");

			// Whatever a handler wrote before it stopped still has to be flushed.
			Assert.That(NumberOf(view, "faultedValue"), Is.EqualTo(lastThrown));
			Assert.That(NumberOf(view, "declinedValue"), Is.EqualTo(lastDeclined));
		});

		var replayed = initial;

		foreach (var patch in view.DrainPatches())
		{
			var result = UiTreeApplier.Apply(replayed, patch);

			Assert.That(result.IsApplied, Is.True, result.RejectionReason);

			replayed = result.Tree;
		}

		Assert.That(UiCanonicalJson.Serialize(replayed), Is.EqualTo(UiCanonicalJson.Serialize(view.Tree)));
	}

	[Test]
	[CancelAfter(30_000)]
	public void A_HandlerFaulted_subscriber_may_itself_dispatch_and_write()
	{
		var written = new UiState<double>(0);
		var failures = new ConcurrentBag<Exception>();
		var accepted = 0;

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					NumberNode("written", () => written.Value),
					new UiStringInput
					{
						Key = "thrower",
						Binding = Bind.ReadOnly(UiValue.From(() => "thrower")),
						Events =
						[
							UiEventHandler.On(UiConfigEvents.Change,
								() => throw new InvalidOperationException("boom")),
						],
					},
					new UiStringInput
					{
						Key = "accepting",
						Binding = Bind.ReadOnly(UiValue.From(() => "accepting")),
						Events = [UiEventHandler.On(UiConfigEvents.Change, () => { })],
					},
				],
			});

		var reentered = false;

		view.HandlerFaulted += (_, _) =>
		{
			if (reentered)
			{
				return;
			}

			reentered = true;

			if (view.Dispatch(ChangeEvent("accepting", "x")).IsAccepted)
			{
				accepted++;
			}

			written.Value = 5;
		};

		RunConcurrently(failures, () => view.Dispatch(ChangeEvent("thrower", "x")));

		AssertNoWorkerFaulted(failures);

		Assert.Multiple(() =>
		{
			Assert.That(reentered, Is.True, "the fault subscriber never ran");
			Assert.That(accepted, Is.EqualTo(1), "a dispatch made from inside a fault subscriber has to be accepted");
			Assert.That(NumberOf(view, "written"), Is.EqualTo(5d));
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public void A_wedged_provider_on_one_view_does_not_freeze_another_view()
	{
		var first = new UiState<double>(0);
		var second = new UiState<double>(0);
		var failures = new ConcurrentBag<Exception>();

		using var providerEntered = new ManualResetEventSlim(false);
		using var release = new ManualResetEventSlim(false);
		using var secondDone = new ManualResetEventSlim(false);

		var viewOne = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "one",
				Children =
				[
					NumberNode("first",
						() =>
						{
							var value = first.Value;

							if (value == 1)
							{
								providerEntered.Set();

								if (!release.Wait(WaitTimeout))
								{
									throw new TimeoutException("the test never released the wedged provider");
								}
							}

							return value;
						}),
				],
			});

		var viewTwo = new UiView(ConfigSurface(),
			new UiFlow { Key = "two", Children = [NumberNode("second", () => second.Value)] });

		viewOne.DrainPatches();
		viewTwo.DrainPatches();

		var wedged = new Thread(() =>
		{
			try
			{
				first.Value = 1;
			}
#pragma warning disable CA1031 // See RunConcurrently: a worker's fault has to reach the test thread.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				failures.Add(exception);
			}
		})
		{
			IsBackground = true,
			Name = "wedged-writer",
		};

		wedged.Start();

		Assert.That(providerEntered.Wait(WaitTimeout), Is.True, "view one's provider never parked");

		var independent = new Thread(() =>
		{
			try
			{
				second.Value = 1;

				if (viewTwo.DrainPatches().Count > 0)
				{
					secondDone.Set();
				}
			}
#pragma warning disable CA1031 // See RunConcurrently.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				failures.Add(exception);
			}
		})
		{
			IsBackground = true,
			Name = "independent-writer",
		};

		independent.Start();

		// The counterexample against a single process-wide lock: view one is still parked inside its provider
		// at this instant, and view two has to have gone all the way through a write and a drain regardless.
		Assert.That(secondDone.Wait(TimeSpan.FromSeconds(5)),
			Is.True,
			"a wedged provider on one view must not block another");

		release.Set();

		Assert.That(independent.Join(WaitTimeout), Is.True, "the independent writer never finished");
		Assert.That(wedged.Join(WaitTimeout), Is.True, "the wedged writer never finished");

		AssertNoWorkerFaulted(failures);

		Assert.Multiple(() =>
		{
			Assert.That(NumberOf(viewOne, "first"), Is.EqualTo(1d));
			Assert.That(NumberOf(viewTwo, "second"), Is.EqualTo(1d));
			Assert.That(viewOne.DrainPatches(), Is.Not.Empty, "view one never emitted the patch it was parked in");
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public void One_state_read_by_two_views_flushes_both_correctly_and_independently()
	{
		var shared = new UiState<double>(0);
		var failures = new ConcurrentBag<Exception>();

		var viewA = new UiView(ConfigSurface(),
			new UiFlow { Key = "a", Children = [NumberNode("aField", () => shared.Value)] });

		var viewB = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "b",
				Children =
				[
					NumberNode("bField", () => shared.Value),
					new UiStringInput
					{
						Key = "bButton",
						Binding = Bind.ReadOnly(UiValue.From(() => "b")),
						Events = [UiEventHandler.On(UiConfigEvents.Change, () => { })],
					},
				],
			});

		var treeA = viewA.Tree;
		var treeB = viewB.Tree;

		viewA.DrainPatches();
		viewB.DrainPatches();

		var revisionA = viewA.Revision;
		var revisionB = viewB.Revision;
		var dispatch = UiDispatchResult.Ignored("never ran");

		RunConcurrently(failures,
			() => shared.Value = 42,
			() => dispatch = viewB.Dispatch(ChangeEvent("bButton", "b")));

		AssertNoWorkerFaulted(failures);

		var patchesA = viewA.DrainPatches();
		var patchesB = viewB.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(dispatch.IsAccepted, Is.True, dispatch.Reason);
			Assert.That(viewA.Revision, Is.EqualTo(revisionA + 1));
			Assert.That(viewB.Revision, Is.EqualTo(revisionB + 1));
			Assert.That(AllOperations(patchesA).Select(operation => operation.NodeId), Has.All.EqualTo("aField"));
			Assert.That(AllOperations(patchesB).Select(operation => operation.NodeId), Has.All.EqualTo("bField"));
			Assert.That(NumberOf(viewA, "aField"), Is.EqualTo(42d));
			Assert.That(NumberOf(viewB, "bField"), Is.EqualTo(42d));
		});

		foreach (var patch in patchesA)
		{
			var applied = UiTreeApplier.Apply(treeA, patch);

			Assert.That(applied.IsApplied, Is.True, applied.RejectionReason);

			treeA = applied.Tree;
		}

		foreach (var patch in patchesB)
		{
			var applied = UiTreeApplier.Apply(treeB, patch);

			Assert.That(applied.IsApplied, Is.True, applied.RejectionReason);

			treeB = applied.Tree;
		}

		Assert.Multiple(() =>
		{
			Assert.That(UiCanonicalJson.Serialize(treeA), Is.EqualTo(UiCanonicalJson.Serialize(viewA.Tree)));
			Assert.That(UiCanonicalJson.Serialize(treeB), Is.EqualTo(UiCanonicalJson.Serialize(viewB.Tree)));
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public void An_invalidation_storm_leaves_every_later_write_with_a_patch_of_its_own()
	{
		// Defeats "drop or coalesce invalidations so the write only appears to work". Phase one asserts
		// nothing about patches at all - it exists only to drive the invalidation path hard - and phase two
		// is fully deterministic: nine patches is a failure.
		const int iterations = 5000;
		const int tail = 10;
		const int tailBase = 1_000_000;

		var state = new UiState<double>(0);
		var failures = new ConcurrentBag<Exception>();
		var view = new UiView(ConfigSurface(),
			new UiFlow { Key = "setup", Children = [NumberNode("stormed", () => state.Value)] });

		var bodies = new Action[4];

		for (var worker = 0; worker < bodies.Length; worker++)
		{
			var offset = worker * iterations;

			bodies[worker] = () =>
			{
				for (var index = 0; index < iterations; index++)
				{
					state.Value = offset + index;
				}
			};
		}

		RunConcurrently(failures, bodies);

		AssertNoWorkerFaulted(failures);

		view.DrainPatches();

		var patches = new List<UiPatch>();

		for (var index = 0; index < tail; index++)
		{
			state.Value = tailBase + index;
			patches.AddRange(view.DrainPatches());
		}

		Assert.That(patches,
			Has.Count.EqualTo(tail),
			"an invalidation the storm swallowed costs a later write its patch");

		Assert.Multiple(() =>
		{
			for (var index = 0; index < patches.Count; index++)
			{
				var patch = patches[index];

				Assert.That(patch.Operations, Has.Count.EqualTo(1), $"patch {index}");
				Assert.That(patch.Operations[0].Op, Is.EqualTo(UiPatchOperations.SetProperties), $"patch {index}");
				Assert.That(patch.Operations[0].NodeId, Is.EqualTo("stormed"), $"patch {index}");
				Assert.That(patch.Operations[0].Properties![UiConfigProperties.Value].GetDouble(),
					Is.EqualTo((double)(tailBase + index)),
					$"patch {index}");

				if (index > 0)
				{
					Assert.That(patch.FromRevision, Is.EqualTo(patches[index - 1].ToRevision), $"patch {index}");
				}
			}

			Assert.That(NumberOf(view, "stormed"), Is.EqualTo((double)(tailBase + tail - 1)));
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public void A_batch_open_on_one_thread_still_coalesces_into_one_patch_while_another_thread_writes()
	{
		const int batched = 5;
		const int others = 3;

		var mine = new UiState<double>[batched];
		var theirs = new UiState<double>[others];
		var children = new List<UiElement>();

		for (var index = 0; index < batched; index++)
		{
			var state = new UiState<double>(0);
			mine[index] = state;
			children.Add(NumberNode($"mine{index}", () => state.Value));
		}

		for (var index = 0; index < others; index++)
		{
			var state = new UiState<double>(0);
			theirs[index] = state;
			children.Add(NumberNode($"theirs{index}", () => state.Value));
		}

		var failures = new ConcurrentBag<Exception>();
		var view = new UiView(ConfigSurface(), new UiFlow { Key = "setup", Children = children });

		view.DrainPatches();

		using var otherDone = new ManualResetEventSlim(false);
		var insideBatch = Array.Empty<UiPatchOperation>();
		var revisionBeforeClose = 0;

		RunConcurrently(failures,
			() =>
			{
				var scope = view.Batch();

				for (var index = 0; index < batched; index++)
				{
					mine[index].Value = index + 1;
				}

				insideBatch = [.. AllOperations(view.DrainPatches())];

				if (!otherDone.Wait(WaitTimeout))
				{
					throw new TimeoutException("the other writer never finished");
				}

				revisionBeforeClose = view.Revision;
				scope.Dispose();
			},
			() =>
			{
				for (var index = 0; index < others; index++)
				{
					theirs[index].Value = index + 1;
				}

				otherDone.Set();
			});

		AssertNoWorkerFaulted(failures);

		var patches = view.DrainPatches();

		// Deliberately silent on whether the other thread's writes were held by this batch too: the design
		// makes the batch view-wide, so they are, but this test is not the place that fixes that either way.
		Assert.That(insideBatch.Select(operation => operation.NodeId),
			Has.None.AnyOf(Enumerable.Range(0, batched).Select(index => $"mine{index}").ToArray()),
			"a batch has to defer its own writes");

		Assert.That(patches, Has.Count.EqualTo(1));

		Assert.Multiple(() =>
		{
			Assert.That(patches[0].FromRevision, Is.EqualTo(revisionBeforeClose));
			Assert.That(patches[0].ToRevision, Is.EqualTo(revisionBeforeClose + 1));
			Assert.That(patches[0].Operations.Select(operation => operation.NodeId),
				Is.SupersetOf(Enumerable.Range(0, batched).Select(index => $"mine{index}")));
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public void A_handler_writing_a_state_only_another_view_reads_notifies_it_before_the_dispatch_returns()
	{
		var foreign = new UiState<double>(0);

		var viewA = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "a",
				Children =
				[
					new UiStringInput
					{
						Key = "aButton",
						Binding = Bind.ReadOnly(UiValue.From(() => "a")),
						Events = [UiEventHandler.On(UiConfigEvents.Change, () => foreign.Value = 9)],
					},
				],
			});

		var viewB = new UiView(ConfigSurface(),
			new UiFlow { Key = "b", Children = [NumberNode("bField", () => foreign.Value)] });

		viewA.DrainPatches();
		viewB.DrainPatches();

		var changed = 0;
		viewB.Changed += (_, _) => Interlocked.Increment(ref changed);

		var result = viewA.Dispatch(ChangeEvent("aButton", "a"));
		var patches = viewB.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.True, result.Reason);
			Assert.That(changed,
				Is.GreaterThanOrEqualTo(1),
				"the foreign view was not notified before Dispatch returned");
			Assert.That(patches, Is.Not.Empty, "the foreign view had no patch queued when Dispatch returned");
			Assert.That(NumberOf(viewB, "bField"), Is.EqualTo(9d));
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public void A_foreign_view_with_an_open_batch_receives_the_cross_component_notification_when_it_closes()
	{
		var foreign = new UiState<double>(0);

		var viewA = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "a",
				Children =
				[
					new UiStringInput
					{
						Key = "aButton",
						Binding = Bind.ReadOnly(UiValue.From(() => "a")),
						Events = [UiEventHandler.On(UiConfigEvents.Change, () => foreign.Value = 9)],
					},
				],
			});

		var viewB = new UiView(ConfigSurface(),
			new UiFlow { Key = "b", Children = [NumberNode("bField", () => foreign.Value)] });

		viewA.DrainPatches();
		viewB.DrainPatches();

		var scope = viewB.Batch();
		var result = viewA.Dispatch(ChangeEvent("aButton", "a"));
		var whileOpen = viewB.DrainPatches();

		scope.Dispose();

		var afterClose = viewB.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.True, result.Reason);
			Assert.That(whileOpen, Is.Empty, "an open batch defers the foreign view's patch");
			Assert.That(afterClose, Is.Not.Empty, "closing the batch lost the cross-component notification");
			Assert.That(NumberOf(viewB, "bField"), Is.EqualTo(9d));
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public void Deferred_work_that_enqueues_further_deferred_work_still_drains()
	{
		var stateB = new UiState<double>(0);
		var stateC = new UiState<double>(0);

		var viewC = new UiView(ConfigSurface(),
			new UiFlow { Key = "c", Children = [NumberNode("cField", () => stateC.Value)] });

		var viewB = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "b",
				Children =
				[
					NumberNode("bField",
						() =>
						{
							var value = stateB.Value;
							stateC.Value = value + 1;

							return value;
						}),
				],
			});

		var viewA = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "a",
				Children =
				[
					new UiStringInput
					{
						Key = "aButton",
						Binding = Bind.ReadOnly(UiValue.From(() => "a")),
						Events = [UiEventHandler.On(UiConfigEvents.Change, () => stateB.Value = 3)],
					},
				],
			});

		viewA.DrainPatches();
		viewB.DrainPatches();
		viewC.DrainPatches();

		var result = viewA.Dispatch(ChangeEvent("aButton", "a"));

		Assert.Multiple(() =>
		{
			Assert.That(result.IsAccepted, Is.True, result.Reason);
			Assert.That(NumberOf(viewB, "bField"), Is.EqualTo(3d));
			Assert.That(NumberOf(viewC, "cField"), Is.EqualTo(4d));
			Assert.That(viewB.DrainPatches(), Is.Not.Empty);
			Assert.That(viewC.DrainPatches(), Is.Not.Empty);
			Assert.That(viewA.PendingWorkCount, Is.Zero);
			Assert.That(viewB.PendingWorkCount, Is.Zero);
			Assert.That(viewC.PendingWorkCount, Is.Zero);
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public void A_view_constructed_over_a_state_another_view_already_reads_still_receives_invalidations()
	{
		var shared = new UiState<double>(0);

		var viewA = new UiView(ConfigSurface(),
			new UiFlow { Key = "a", Children = [NumberNode("aField", () => shared.Value)] });

		var viewB = new UiView(ConfigSurface(),
			new UiFlow { Key = "b", Children = [NumberNode("bField", () => shared.Value)] });

		viewA.DrainPatches();
		viewB.DrainPatches();

		// One write and nothing else: an implementation that records the deferred merge but never
		// re-evaluates the newly attached view leaves view B silent here.
		shared.Value = 11;

		var patches = viewB.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(patches, Is.Not.Empty, "the freshly constructed view was never invalidated");
			Assert.That(AllOperations(patches)
					.Where(operation => string.Equals(operation.NodeId, "bField", StringComparison.Ordinal))
					.Select(operation => operation.Properties![UiConfigProperties.Value].GetDouble()),
				Does.Contain(11d));
			Assert.That(NumberOf(viewB, "bField"), Is.EqualTo(11d));
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public async Task A_view_attaching_to_an_async_state_with_a_load_in_flight_does_not_settle_early()
	{
		var completion = new TaskCompletionSource<string>();
		var state = new UiAsyncState<string>(_ => completion.Task, "initial");

		var viewA = new UiView(ConfigSurface(),
			new UiFlow { Key = "a", Children = [TextNode("aField", () => state.Value)] });

		state.Reload();

		var viewB = new UiView(ConfigSurface(),
			new UiFlow { Key = "b", Children = [TextNode("bField", () => state.Value)] });

		var idle = viewB.WhenIdleAsync(TestContext.CurrentContext.CancellationToken);

		Assert.Multiple(() =>
		{
			// Nothing can complete the load but the test, so a settled task here is a view that never saw the
			// work it had just attached to.
			Assert.That(viewB.PendingWorkCount, Is.GreaterThan(0), "the in-flight load was not handed to the new view");
			Assert.That(idle.IsCompleted, Is.False, "the attaching view settled on a load that is still running");
		});

		completion.SetResult("loaded");

		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		await idle.WaitAsync(cancellation.Token);
		await viewA.WhenIdleAsync(cancellation.Token);

		Assert.That(FindById(viewB.Tree.Root, "bField")!.Properties[UiConfigProperties.Value].GetString(),
			Is.EqualTo("loaded"));
	}

	[Test]
	[CancelAfter(30_000)]
	public void A_constructor_that_throws_while_another_thread_writes_leaves_the_states_usable()
	{
		const int iterations = 200;

		var state = new UiState<double>(0);
		var failures = new ConcurrentBag<Exception>();
		var thrown = 0;

		RunConcurrently(failures,
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					try
					{
						// Two children with one key: the first reads the state and the second is a duplicate
						// id, so materialization faults after the dependency was already recorded.
						_ = new UiView(ConfigSurface(),
							new UiFlow
							{
								Key = "broken",
								Children = [NumberNode("dup", () => state.Value), NumberNode("dup", () => 0)],
							});
					}
					catch (UiViewException)
					{
						thrown++;
					}
				}
			},
			() =>
			{
				for (var value = 1; value <= iterations; value++)
				{
					state.Value = value;
				}
			});

		AssertNoWorkerFaulted(failures);

		Assert.That(thrown, Is.EqualTo(iterations), "the fixture has to fault every time for this to prove anything");

		var view = new UiView(ConfigSurface(),
			new UiFlow { Key = "good", Children = [NumberNode("goodField", () => state.Value)] });

		view.DrainPatches();
		state.Value = 999;

		Assert.Multiple(() =>
		{
			Assert.That(view.DrainPatches(), Is.Not.Empty, "the abandoned constructors left the state unusable");
			Assert.That(NumberOf(view, "goodField"), Is.EqualTo(999d));
		});
	}

	/// <summary>
	/// Two views cross-linked through their asynchronous states: each one's handler reloads the state the
	/// other view reads. Reloading registers the load as pending work on every view reading it, and doing
	/// that from a handler means doing it while already inside the dispatching view's own serialization - so
	/// an implementation that takes the second view's lock there wedges both threads for good. The registration
	/// has to reach the foreign view all the same, which is what the next test pins.
	/// </summary>
	[Test]
	[CancelAfter(30_000)]
	public void Handlers_that_reload_each_other_s_async_state_never_wedge_each_other()
	{
		const int iterations = 2000;

		var never = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		var readByB = new UiAsyncState<string>(_ => never.Task, "x");
		var readByA = new UiAsyncState<string>(_ => never.Task, "y");
		var failures = new ConcurrentBag<Exception>();

		var viewA = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "a",
				Children =
				[
					TextNode("aCell", () => readByA.Value),
					new UiStringInput
					{
						Key = "aTrigger",
						Binding = Bind.ReadOnly(UiValue.Of("go")),
						Events = [UiEventHandler.On(UiConfigEvents.Change, () => readByB.Reload())],
					},
				],
			});

		var viewB = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "b",
				Children =
				[
					TextNode("bCell", () => readByB.Value),
					new UiStringInput
					{
						Key = "bTrigger",
						Binding = Bind.ReadOnly(UiValue.Of("go")),
						Events = [UiEventHandler.On(UiConfigEvents.Change, () => readByA.Reload())],
					},
				],
			});

		RunConcurrently(failures,
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					var result = viewA.Dispatch(ChangeEvent("aTrigger", "go"));

					if (!result.IsAccepted)
					{
						throw new InvalidOperationException($"view A's dispatch was refused: {result.Reason}");
					}
				}
			},
			() =>
			{
				for (var index = 0; index < iterations; index++)
				{
					var result = viewB.Dispatch(ChangeEvent("bTrigger", "go"));

					if (!result.IsAccepted)
					{
						throw new InvalidOperationException($"view B's dispatch was refused: {result.Reason}");
					}
				}
			});

		AssertNoWorkerFaulted(failures);

		never.SetResult("done");
	}

	/// <summary>
	/// The property the cheap repair for the test above would have broken. A handler on one view reloads a
	/// state only a second view reads, so the registration crosses components; if it is skipped rather than
	/// deferred, the second view believes it has nothing to wait for and settles on a load that has not
	/// happened - the exact failure <c>UiState.ViewAttached</c> exists to prevent.
	/// </summary>
	[Test]
	[CancelAfter(30_000)]
	public async Task A_load_started_from_a_foreign_view_s_handler_still_holds_that_view_s_settling()
	{
		var gate = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		var loaded = new UiAsyncState<string>(_ => gate.Task, "initial");

		var reader = new UiView(ConfigSurface(),
			new UiFlow { Key = "reader", Children = [TextNode("cell", () => loaded.Value)] });

		var trigger = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "trigger",
				Children =
				[
					new UiStringInput
					{
						Key = "reload",
						Binding = Bind.ReadOnly(UiValue.Of("go")),
						Events = [UiEventHandler.On(UiConfigEvents.Change, () => loaded.Reload())],
					},
				],
			});

		reader.DrainPatches();

		Assert.That(trigger.Dispatch(ChangeEvent("reload", "go")).IsAccepted, Is.True);

		using var cancellation = new CancellationTokenSource(WaitTimeout);
		var settling = reader.WhenIdleAsync(cancellation.Token);

		// Deterministic rather than timed: with the load registered, WhenIdleAsync awaits it and cannot have
		// completed by the time it returned its task. A registration that was dropped instead leaves nothing
		// pending, and the call completes synchronously.
		Assert.That(settling.IsCompleted,
			Is.False,
			"the reader settled on a load that is still running - its registration never arrived");

		gate.SetResult("loaded");

		await settling;

		Assert.Multiple(() =>
		{
			Assert.That(loaded.Peek(), Is.EqualTo("loaded"));
			Assert.That(FindById(reader.Tree.Root, "cell")!.Properties[UiConfigProperties.Value].GetString(),
				Is.EqualTo("loaded"));
		});
	}
}
