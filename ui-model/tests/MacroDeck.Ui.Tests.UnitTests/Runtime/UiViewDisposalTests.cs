using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using static MacroDeck.Ui.Tests.UnitTests.Runtime.UiConcurrency;

namespace MacroDeck.Ui.Tests.UnitTests.Runtime;

[TestFixture]
public class UiViewDisposalTests
{
	private static UiSurface ConfigSurface()
		=> new() { Kind = UiSurfaceKinds.Config, SessionMode = UiSessionModes.Exclusive };

	private static UiFlow Reading(Func<string> value)
		=> new()
		{
			Key = "setup",
			Children = [new UiStringInput { Key = "shown", Binding = Bind.ReadOnly(UiValue.From(value)) }],
		};

	private static void CollectGarbage()
	{
		GC.Collect();
		GC.WaitForPendingFinalizers();
		GC.Collect();
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference OpenAndDispose(UiState<string> shared)
	{
		var view = new UiView(ConfigSurface(), Reading(() => shared.Value));
		view.DrainPatches();
		view.Dispose();

		return new WeakReference(view);
	}

	[Test]
	public void A_disposed_view_reading_a_long_lived_state_can_be_collected()
	{
		var shared = new UiState<string>("frame-0");
		var released = Enumerable.Range(0, 20).Select(_ => OpenAndDispose(shared)).ToList();

		for (var frame = 1; frame <= 10; frame++)
		{
			shared.Value = $"frame-{frame}";
		}

		CollectGarbage();

		Assert.That(released.Count(reference => reference.IsAlive), Is.Zero);
	}

	[Test]
	public void After_disposal_a_write_reaches_only_the_views_still_open()
	{
		var shared = new UiState<string>("before");
		var closed = new UiView(ConfigSurface(), Reading(() => shared.Value));
		var open = new UiView(ConfigSurface(), Reading(() => shared.Value));
		var closedChanges = 0;
		closed.Changed += (_, _) => closedChanges++;

		closed.Dispose();
		shared.Value = "after";

		Assert.Multiple(() =>
		{
			Assert.That(closedChanges, Is.Zero);
			Assert.That(closed.DrainPatches(), Is.Empty);
			Assert.That(open.DrainPatches(), Has.Count.EqualTo(1));
			Assert.That(open.Tree.Root.Children[0].Properties["value"].GetString(), Is.EqualTo("after"));
		});
	}

	[Test]
	public void A_disposed_view_ignores_events_and_keeps_answering_with_its_last_tree()
	{
		var shared = new UiState<string>("value");
		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStep
					{
						Key = "step",
						Events = [UiEventHandler.On(UiConfigEvents.Submit, () => shared.Value = "submitted")],
					},
				],
			});
		var tree = view.Tree;

		view.Dispose();
		view.Dispose();
		var result = view.Dispatch(new UiEvent { NodeId = "setup.step", Name = UiConfigEvents.Submit });

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(UiDispatchOutcome.Ignored));
			Assert.That(shared.Peek(), Is.EqualTo("value"));
			Assert.That(view.Tree, Is.SameAs(tree));
			Assert.That(view.Revision, Is.Zero);
			Assert.That(view.DrainPatches(), Is.Empty);
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static (UiView Closer, StrongBox<UiView?> Target, WeakReference Released) BuildCloser(
		UiState<string> shared)
	{
		var target = new StrongBox<UiView?>(new UiView(ConfigSurface(), Reading(() => shared.Value)));
		var released = new WeakReference(target.Value);
		var closerState = new UiState<string>("open");

		var closer = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput { Key = "own", Binding = Bind.To(closerState) },
					new UiStep
					{
						Key = "step",
						Events =
						[
							UiEventHandler.On(UiConfigEvents.Submit,
								() =>
								{
									closerState.Value = "closing";
									target.Value!.Dispose();
									target.Value = null;
								}),
						],
					},
				],
			});

		return (closer, target, released);
	}

	[Test]
	public void A_view_disposed_from_a_handler_of_another_view_is_released_once_that_dispatch_returns()
	{
		var shared = new UiState<string>("before");
		var (closer, _, released) = BuildCloser(shared);

		var result = closer.Dispatch(new UiEvent { NodeId = "setup.step", Name = UiConfigEvents.Submit });
		shared.Value = "after";
		CollectGarbage();

		Assert.Multiple(() =>
		{
			Assert.That(result.Outcome, Is.EqualTo(UiDispatchOutcome.Accepted));
			Assert.That(released.IsAlive, Is.False);
		});

		GC.KeepAlive(closer);
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference OpenViewThatClosesItself(UiState<string> shared)
	{
		var self = new StrongBox<UiView?>();
		self.Value = new UiView(ConfigSurface(),
			Reading(() =>
			{
				if (shared.Value == "close")
				{
					self.Value?.Dispose();
				}

				return shared.Value;
			}));

		var released = new WeakReference(self.Value);
		self.Value.Changed += (_, _) => throw new AssertionException("A disposed view raised Changed.");

		return released;
	}

	[Test]
	public void A_view_disposed_by_its_own_value_provider_during_a_flush_emits_nothing_and_is_released()
	{
		var shared = new UiState<string>("open");
		var released = OpenViewThatClosesItself(shared);

		Assert.DoesNotThrow(() => shared.Value = "close");
		shared.Value = "after";
		CollectGarbage();

		Assert.That(released.IsAlive, Is.False);
	}

	[Test]
	public void A_view_disposed_from_its_own_Changed_subscriber_raises_nothing_afterwards()
	{
		var shared = new UiState<string>("0");
		var view = new UiView(ConfigSurface(), Reading(() => shared.Value));
		var changes = 0;
		view.Changed += (_, _) =>
		{
			changes++;
			view.Dispose();
		};

		shared.Value = "1";
		shared.Value = "2";

		Assert.Multiple(() =>
		{
			Assert.That(changes, Is.EqualTo(1));
			Assert.That(view.DrainPatches(), Is.Empty);
		});
	}

	[Test]
	public void Closing_a_batch_on_a_view_disposed_inside_it_emits_nothing()
	{
		var shared = new UiState<string>("before");
		var view = new UiView(ConfigSurface(), Reading(() => shared.Value));
		var changes = 0;
		view.Changed += (_, _) => changes++;

		var batch = view.Batch();
		shared.Value = "inside";
		view.Dispose();

		Assert.DoesNotThrow(batch.Dispose);
		Assert.Multiple(() =>
		{
			Assert.That(changes, Is.Zero);
			Assert.That(view.DrainPatches(), Is.Empty);
		});
	}

	[Test]
	public void Disposing_while_another_thread_writes_the_shared_state_leaves_the_open_view_consistent()
	{
		const int writes = 2000;
		var shared = new UiState<int>(0);
		var closing = new UiView(ConfigSurface(), Reading(() => shared.Value.ToString(CultureInfo.InvariantCulture)));
		var open = new UiView(ConfigSurface(), Reading(() => shared.Value.ToString(CultureInfo.InvariantCulture)));
		var failures = new ConcurrentBag<Exception>();

		RunConcurrently(failures,
			() =>
			{
				for (var write = 1; write <= writes; write++)
				{
					shared.Value = write;
				}
			},
			() =>
			{
				Thread.Yield();
				closing.Dispose();
			});

		AssertNoWorkerFaulted(failures);

		var closingChanges = 0;
		closing.Changed += (_, _) => closingChanges++;
		closing.DrainPatches();
		shared.Value = writes + 1;

		Assert.Multiple(() =>
		{
			Assert.That(closingChanges, Is.Zero);
			Assert.That(closing.DrainPatches(), Is.Empty);
			Assert.That(open.Tree.Root.Children[0].Properties["value"].GetString(),
				Is.EqualTo((writes + 1).ToString(CultureInfo.InvariantCulture)));
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference OpenAndDisposeDuringLoad(UiAsyncState<string> state)
	{
		var view = new UiView(ConfigSurface(), Reading(() => state.Value));
		view.DrainPatches();
		state.Reload();
		view.Dispose();

		return new WeakReference(view);
	}

	[Test]
	public async Task A_load_finishing_after_disposal_reaches_only_the_views_still_open()
	{
		var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		var state = new UiAsyncState<string>(_ => completion.Task, "initial");
		var released = OpenAndDisposeDuringLoad(state);
		var open = new UiView(ConfigSurface(), Reading(() => state.Value));

		completion.SetResult("loaded");
		await open.WhenIdleAsync().WaitAsync(WaitTimeout);
		CollectGarbage();

		Assert.Multiple(() =>
		{
			Assert.That(released.IsAlive, Is.False);
			Assert.That(open.Tree.Root.Children[0].Properties["value"].GetString(), Is.EqualTo("loaded"));
		});
	}

	[Test]
	public void A_value_that_stops_and_resumes_reading_a_state_updates_again_while_another_view_never_stopped()
	{
		var reading = new UiState<bool>(true);
		var shared = new UiState<string>("a");
		var toggling = new UiView(ConfigSurface(), Reading(() => reading.Value ? shared.Value : "off"));
		var steady = new UiView(ConfigSurface(), Reading(() => shared.Value));

		reading.Value = false;
		toggling.DrainPatches();
		shared.Value = "b";
		var whileNotReading = toggling.DrainPatches();
		var steadyWhileOtherNotReading = steady.DrainPatches();

		reading.Value = true;
		toggling.DrainPatches();
		shared.Value = "c";

		Assert.Multiple(() =>
		{
			Assert.That(whileNotReading, Is.Empty);
			Assert.That(steadyWhileOtherNotReading, Has.Count.EqualTo(1));
			Assert.That(toggling.DrainPatches(), Has.Count.EqualTo(1));
			Assert.That(toggling.Tree.Root.Children[0].Properties["value"].GetString(), Is.EqualTo("c"));
		});
	}

	private static UiFlow Gated(UiState<bool> visible, Func<UiElement> content)
		=> new()
		{
			Key = "setup",
			Children = [new UiWhen { Key = "gate", Condition = () => visible.Value, Content = content }],
		};

	[Test]
	public void Content_reading_a_long_lived_state_that_is_hidden_and_shown_again_still_updates()
	{
		var visible = new UiState<bool>(true);
		var shared = new UiState<string>("a");
		var view = new UiView(ConfigSurface(),
			Gated(visible,
				() => new UiStringInput { Key = "shown", Binding = Bind.ReadOnly(UiValue.From(() => shared.Value)) }));

		visible.Value = false;
		visible.Value = true;
		view.DrainPatches();
		shared.Value = "b";

		Assert.Multiple(() =>
		{
			Assert.That(view.DrainPatches(), Has.Count.EqualTo(1));
			Assert.That(view.Tree.Root.Children[0].Properties["value"].GetString(), Is.EqualTo("b"));
		});
	}

	[MethodImpl(MethodImplOptions.NoInlining)]
	private static WeakReference OpenAndHideContent(UiState<string> shared)
	{
		var visible = new UiState<bool>(true);
		var view = new UiView(ConfigSurface(),
			Gated(visible,
				() => new UiStringInput { Key = "shown", Binding = Bind.ReadOnly(UiValue.From(() => shared.Value)) }));

		visible.Value = false;

		return new WeakReference(view);
	}

	[Test]
	public void A_state_read_only_by_removed_content_no_longer_keeps_an_undisposed_view_alive()
	{
		var shared = new UiState<string>("a");
		var released = OpenAndHideContent(shared);

		shared.Value = "b";
		CollectGarbage();

		Assert.That(released.IsAlive, Is.False);
	}

	[Test]
	public void States_read_by_cells_of_removed_content_are_not_kept_alive_by_the_live_view()
	{
		var visible = new UiState<bool>(true);
		var created = new List<WeakReference>();

		var view = new UiView(ConfigSurface(),
			Gated(visible,
				() =>
				{
					var fresh = new UiState<string>("fresh");
					created.Add(new WeakReference(fresh));

					return new UiStringInput { Key = "shown", Binding = Bind.ReadOnly(UiValue.From(() => fresh.Value)) };
				}));

		for (var round = 0; round < 10; round++)
		{
			visible.Value = false;
			visible.Value = true;
		}

		CollectGarbage();

		Assert.That(created.Count(reference => reference.IsAlive), Is.EqualTo(1));

		GC.KeepAlive(view);
	}

	[Test]
	public async Task A_view_that_reads_a_loading_state_again_waits_for_the_load_started_while_it_was_not_reading()
	{
		var reading = new UiState<bool>(true);
		var completion = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
		var state = new UiAsyncState<string>(_ => completion.Task, "initial");
		var view = new UiView(ConfigSurface(), Reading(() => reading.Value ? state.Value : "not reading"));

		reading.Value = false;
		state.Reload();
		reading.Value = true;

		var idle = view.WhenIdleAsync();
		var waitedForLoad = !idle.IsCompleted;

		completion.SetResult("loaded");
		await idle.WaitAsync(WaitTimeout);

		Assert.Multiple(() =>
		{
			Assert.That(waitedForLoad, Is.True);
			Assert.That(view.Tree.Root.Children[0].Properties["value"].GetString(), Is.EqualTo("loaded"));
		});
	}
}
