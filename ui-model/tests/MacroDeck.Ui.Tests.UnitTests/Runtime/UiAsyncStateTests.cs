using System.Collections.Concurrent;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using static MacroDeck.Ui.Tests.UnitTests.Runtime.UiConcurrency;

namespace MacroDeck.Ui.Tests.UnitTests.Runtime;

/// <summary>
/// Regression coverage for asynchronously loaded state: the two failure modes a single-load test cannot see.
/// A response landing after the request that superseded it shows the user options for a filter they already
/// changed, and a loader that faults either escapes into whoever rendered the tree or never finishes, so a
/// plugin whose service is down shows a frozen dialog. Each test traces to acceptance scenario 34 or 35 for
/// issue #540.
/// </summary>
[TestFixture]
public class UiAsyncStateTests
{
	private static readonly string[] _expectedOptionValues = ["a"];

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

	[Test]
	public async Task A_stale_in_flight_load_never_overwrites_a_newer_one()
	{
		// A completion source per call, so which load lands first is decided by the test rather than by timing.
		var completions = new List<TaskCompletionSource<string>>();
		var tokens = new List<CancellationToken>();

		var state = new UiAsyncState<string>(cancellationToken =>
			{
				var completion = new TaskCompletionSource<string>();
				completions.Add(completion);
				tokens.Add(cancellationToken);

				return completion.Task;
			},
			string.Empty);

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput
					{
						Key = "loaded",
						Binding = Bind.ReadOnly(UiValue.From(() => state.Value)),
					},
				],
			});

		view.DrainPatches();

		state.Reload();
		state.Reload();

		Assert.That(completions, Has.Count.EqualTo(2), "each reload has to start its own load");

		// The first response arrives second, which is the whole scenario.
		completions[1].SetResult("second");
		completions[0].SetResult("first");

		await view.WhenIdleAsync(TestContext.CurrentContext.CancellationToken);

		var patches = view.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(state.Peek(), Is.EqualTo("second"));
			Assert.That(tokens[0].IsCancellationRequested,
				Is.True,
				"the superseded load has to be cancelled, not merely ignored");
			Assert.That(state.IsLoading, Is.False);
			Assert.That(state.Error, Is.Null);

			// Over the whole drained set, not just the last patch: an apply-then-correct runtime would still have
			// leaked an intermediate revision showing the stale answer.
			foreach (var patch in patches)
			{
				foreach (var operation in patch.Operations)
				{
					var value = operation.Properties?.GetValueOrDefault(UiConfigProperties.Value);

					Assert.That(value?.GetString(),
						Is.Not.EqualTo("first"),
						$"revision {patch.ToRevision} carried the stale value");
				}
			}

			Assert.That(FindById(view.Tree.Root, "loaded")!.Properties[UiConfigProperties.Value].GetString(),
				Is.EqualTo("second"));
		});
	}

	[Test]
	public async Task A_faulting_load_surfaces_as_Error_without_throwing_and_leaves_the_flow_usable()
	{
		var shouldFault = true;

		Func<UiOptionQuery, CancellationToken, Task<UiOptionResult>> load = (_, _) => shouldFault
			? throw new HttpRequestException("the service is down")
			: Task.FromResult(UiOptionResult.From([UiOption.Of("a", "A")]));

		var options = new UiOptionsState(UiOptionSource.From(load));

		UiView? view = null;

		Assert.DoesNotThrow(() => view = new UiView(ConfigSurface(), BuildOptionFlow(options)),
			"a faulting loader must not escape the render");

		Assert.DoesNotThrow(options.Reload, "a faulting loader must not escape the caller that started it");

		await view!.WhenIdleAsync(TestContext.CurrentContext.CancellationToken);

		Assert.Multiple(() =>
		{
			Assert.That(options.IsLoading, Is.False);
			Assert.That(options.Error, Is.EqualTo("the service is down"));
			Assert.That(FindById(view!.Tree.Root, "setup.busy"), Is.Null, "the busy node has to be gone");

			var banner = FindById(view.Tree.Root, "setup.banner");

			Assert.That(banner, Is.Not.Null);
			Assert.That(banner!.Properties[UiConfigProperties.Text].GetString(),
				Is.EqualTo("the service is down"));
		});

		view.DrainPatches();
		shouldFault = false;
		options.Reload();

		await view.WhenIdleAsync(TestContext.CurrentContext.CancellationToken);

		var reloadPatches = view.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(options.Error, Is.Null);

			// The loading key is a presence flag, so finishing a load removes it rather than setting it to false -
			// a present false and an absent key are different things a renderer cannot tell apart afterwards.
			Assert.That(reloadPatches
					.SelectMany(patch => patch.Operations)
					.Any(operation => operation.RemovedProperties?.Contains(UiConfigProperties.Loading) ?? false),
				Is.True);
			Assert.That(options.Options.Select(option => option.Value), Is.EqualTo(_expectedOptionValues).AsCollection);
			Assert.That(FindById(view.Tree.Root, "setup.banner"),
				Is.Null,
				"a successful reload has to remove the banner");
			Assert.That(FindById(view.Tree.Root, "mode")!.Properties.ContainsKey(UiConfigProperties.Loading),
				Is.False,
				"the loading key is present only while a load is running");
		});
	}

	// SettleAsync_times_out_rather_than_hanging_when_a_load_never_completes lives in
	// MacroDeck.Ui.Testing.Tests.UnitTests: the scenario asserts a UiTestTimeoutException, which belongs to
	// MacroDeck.Ui.Testing alongside SettleAsync itself. Asserting a bare TimeoutException here would freeze the
	// wrong contract - neither TimeoutException nor OperationCanceledException is acceptable.

	[Test]
	[CancelAfter(30_000)]
	public async Task A_reload_storm_never_throws_and_settles_once()
	{
		const int workers = 6;
		const int reloadsPerWorker = 200;
		const string settled = "settled";

		var completions = new List<TaskCompletionSource<string>>();
		var recorded = new Lock();
		var invocations = 0;
		var failures = new ConcurrentBag<Exception>();

		var state = new UiAsyncState<string>(_ =>
			{
				Interlocked.Increment(ref invocations);

				var completion = new TaskCompletionSource<string>();

				lock (recorded)
				{
					completions.Add(completion);
				}

				return completion.Task;
			},
			"initial");

		var view = LoadedValueView(state);
		view.DrainPatches();

		RunConcurrently(failures,
			workers,
			_ =>
			{
				for (var index = 0; index < reloadsPerWorker; index++)
				{
					state.Reload();
				}
			});

		// The bag being empty is the assertion: a cancellation meeting a disposal surfaced as an
		// ObjectDisposedException out of Reload, which lands here like any other worker fault.
		AssertNoWorkerFaulted(failures);

		TaskCompletionSource<string>[] outstanding;

		lock (recorded)
		{
			outstanding = [.. completions];
		}

		// Every load is handed the same answer, so "the value the last-started load produced" is well defined
		// however the starts interleaved - which they are free to do, since nothing here orders them.
		foreach (var completion in outstanding)
		{
			completion.TrySetResult(settled);
		}

		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		await view.WhenIdleAsync(cancellation.Token);

		Assert.Multiple(() =>
		{
			Assert.That(state.IsLoading, Is.False);
			Assert.That(state.Error, Is.Null);
			Assert.That(state.Peek(), Is.EqualTo(settled));

			// Defeats "safe by doing nothing": a runtime that dropped reloads would settle just as cleanly.
			Assert.That(invocations, Is.EqualTo(workers * reloadsPerWorker));
		});
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task Cancelling_a_load_that_is_finishing_at_the_same_instant_is_never_observable()
	{
		const int iterations = 200;

		var cancelled = 0;

		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

		for (var iteration = 0; iteration < iterations; iteration++)
		{
			var completions = new List<TaskCompletionSource<string>>();
			var tokens = new List<CancellationToken>();
			var recorded = new Lock();
			var failures = new ConcurrentBag<Exception>();

			var state = new UiAsyncState<string>(token =>
				{
					var completion = new TaskCompletionSource<string>();

					lock (recorded)
					{
						completions.Add(completion);
						tokens.Add(token);
					}

					return completion.Task;
				},
				"initial");

			var view = LoadedValueView(state);
			view.DrainPatches();

			state.Reload();

			Assert.That(completions, Has.Count.EqualTo(1), $"iteration {iteration}");

			var first = completions[0];

			RunConcurrently(failures, () => first.SetResult("first"), state.Reload);

			AssertNoWorkerFaulted(failures);

			Assert.That(completions, Has.Count.EqualTo(2), $"iteration {iteration}");

			completions[1].SetResult("second");

			await view.WhenIdleAsync(cancellation.Token);

			Assert.Multiple(() =>
			{
				// An ObjectDisposedException leaking onto Error is what the user actually saw.
				Assert.That(state.Error, Is.Null, $"iteration {iteration}");
				Assert.That(state.Peek(), Is.EqualTo("second"), $"iteration {iteration}");
				Assert.That(state.IsLoading, Is.False, $"iteration {iteration}");
			});

			if (tokens[0].IsCancellationRequested)
			{
				cancelled++;
			}
		}

		Assert.That(cancelled,
			Is.GreaterThan(0),
			"no iteration ever cancelled the superseded load, so the race under test never happened");
	}

	[Test]
	[CancelAfter(30_000)]
	public async Task A_stale_value_never_appears_on_the_wire_when_the_superseded_load_lands_last()
	{
		var completions = new List<TaskCompletionSource<string>>();
		var recorded = new Lock();
		var failures = new ConcurrentBag<Exception>();

		var state = new UiAsyncState<string>(_ =>
			{
				var completion = new TaskCompletionSource<string>();

				lock (recorded)
				{
					completions.Add(completion);
				}

				return completion.Task;
			},
			"initial");

		var view = LoadedValueView(state);
		view.DrainPatches();

		using var landed = new ManualResetEventSlim(false);
		view.Changed += (_, _) => landed.Set();

		state.Reload();
		state.Reload();

		Assert.That(completions, Has.Count.EqualTo(2));

		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));
		var patches = new List<Model.Patches.UiPatch>();

		// The newer load lands first, from a thread that is not the test's. Settling here would have to wait
		// for the superseded load as well - it is registered work that has not finished - so the intermediate
		// observation point is the patch the winning load queued.
		RunConcurrently(failures, () => completions[1].SetResult("second"));

		Assert.That(landed.Wait(WaitTimeout), Is.True, "the load that won never reached the view");

		patches.AddRange(view.DrainPatches());

		RunConcurrently(failures, () => completions[0].SetResult("first"));

		await view.WhenIdleAsync(cancellation.Token);
		patches.AddRange(view.DrainPatches());

		AssertNoWorkerFaulted(failures);

		Assert.Multiple(() =>
		{
			Assert.That(state.Peek(), Is.EqualTo("second"));
			Assert.That(state.IsLoading, Is.False);
			Assert.That(patches, Is.Not.Empty, "the load that won had to reach the client");

			foreach (var patch in patches)
			{
				foreach (var operation in patch.Operations)
				{
					Assert.That(operation.Properties?.GetValueOrDefault(UiConfigProperties.Value).GetString(),
						Is.Not.EqualTo("first"),
						$"revision {patch.ToRevision} carried the superseded load's value");
				}
			}
		});
	}

	[Test]
	[CancelAfter(30_000)]
	public async Task WhenIdleAsync_settles_on_work_registered_from_another_thread()
	{
		var gates = new[] { new TaskCompletionSource(), new TaskCompletionSource() };
		var started = 0;
		var completed = 0;
		var failures = new ConcurrentBag<Exception>();

		UiAsyncState<int>? state = null;

		state = new UiAsyncState<int>(async _ =>
			{
				var index = Interlocked.Increment(ref started) - 1;

				await gates[Math.Min(index, gates.Length - 1)].Task.ConfigureAwait(false);

				Interlocked.Increment(ref completed);

				if (index == 0)
				{
					// The continuation of the first load starts a second one: settling has to wait for that
					// too rather than returning between the two.
					state!.Reload();
				}

				return index;
			},
			-1);

		var view = new UiView(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiNumberInput
					{
						Key = "loadedNumber",
						Binding = Bind.ReadOnly(UiValue.From(() => (double)state!.Value)),
					},
				],
			});

		view.DrainPatches();

		// Started on another thread and joined before the wait begins: a wait that started before the work was
		// registered would be testing nothing, since there would be nothing to settle on yet.
		RunConcurrently(failures, state.Reload);

		AssertNoWorkerFaulted(failures);

		var observed = -1;

		using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10));

		var idle = Task.Run(async () =>
			{
				await view.WhenIdleAsync(cancellation.Token).ConfigureAwait(false);

				observed = Volatile.Read(ref completed);
			},
			cancellation.Token);

		gates[0].SetResult();
		gates[1].SetResult();

		await idle.WaitAsync(cancellation.Token);

		Assert.Multiple(() =>
		{
			Assert.That(observed,
				Is.EqualTo(2),
				"settling returned before the load the first one started had finished");
			Assert.That(state.IsLoading, Is.False);
			Assert.That(state.Error, Is.Null);
		});
	}

	/// <summary>A view whose one node reads the async state's value, so a load landing is observable as a
	/// patch and a tree rather than only as a property on the state.</summary>
	private static UiView LoadedValueView(UiAsyncState<string> state)
		=> new(ConfigSurface(),
			new UiFlow
			{
				Key = "setup",
				Children =
				[
					new UiStringInput { Key = "loaded", Binding = Bind.ReadOnly(UiValue.From(() => state.Value)) },
				],
			});

	/// <summary>A flow whose busy node exists while the options load and whose banner exists while the load has
	/// failed, so both transitions are observable as structure rather than as a property.</summary>
	private static UiFlow BuildOptionFlow(UiOptionsState options)
		=> new()
		{
			Key = "setup",
			Children =
			[
				new UiWhen
				{
					Key = "loadingGate",
					Condition = () => options.IsLoading,
					Content = () => new UiBusy { Key = "busy", Text = "Loading options..." },
				},
				new UiWhen
				{
					Key = "errorGate",
					Condition = () => options.Error is not null,
					Content = () => new UiBanner
					{
						Key = "banner",
						Severity = "error",
						Text = UiValue.From(() => options.Error ?? string.Empty),
					},
				},
				new UiDynamicChoiceInput { Key = "mode", OptionsState = options },
			],
		};
}
