using System.Collections.Concurrent;
using System.Globalization;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Testing;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Tests.UnitTests.Delegation;
using MacroDeckHost.Widgets.HistoryGraph;
using static MacroDeckHost.Tests.UnitTests.Widgets.Ui.HistoryGraphTestSupport;
using static MacroDeckHost.Tests.UnitTests.Widgets.Ui.UiSessionConcurrency;

namespace MacroDeckHost.Tests.UnitTests.Widgets.Ui;

/// <summary>
/// A graph repaints once a second for as long as it is on screen, so how it repaints is the whole
/// question: the samples have to reach the client without the tree being reconciled, and a session that
/// closed must stop asking for them.
///
/// <para>
/// It is also where issue #830 was reported from: the sampling timer wrote state from its own thread while
/// a socket pump was inside the session, and the runtime aborted the host. The concurrency tests below race
/// that shape deliberately, under bounded waits so a deadlock fails an assertion rather than hanging.
/// </para>
/// </summary>
[TestFixture]
public class HistoryGraphWidgetSessionTests
{
	private static readonly object _config = new
	{
		valueVariable = Metric, title = "CPU Load", subtitle = "{{ vars.system_cpu_name }}", maxValue = 100,
	};

	/// <summary>The card's root and its chart, named by the test rather than read back from the tree: a
	/// renamed node has to fail here rather than quietly assert nothing.</summary>
	private const string _graphNodeId = "historyGraph";

	private const string _chartNodeId = "historyGraph.chart";

	/// <summary>A sample no worker ever pushes, so the tail assertion cannot be satisfied by an idle write.
	/// </summary>
	private const int _distinctSample = 12345;

	[Test]
	public async Task A_new_sample_reaches_the_client_as_a_property_change_rather_than_a_new_tree()
	{
		await using var fixture = new Fixture(_config);

		fixture.Window.Push(10, 20, 30);

		var patches = fixture.Session.DrainPatches();

		Assert.Multiple(() =>
		{
			Assert.That(patches, Is.Not.Empty);
			Assert.That(patches.SelectMany(patch => patch.Operations).Select(operation => operation.Op),
				Has.All.EqualTo(UiPatchOperations.SetProperties),
				"a graph that reconciled its tree once a second would cost more than the data it carries");
		});
	}

	[Test]
	public async Task A_variable_named_only_inside_the_subtitle_still_repaints_the_card()
	{
		var registry = Registry();
		await using var fixture = new Fixture(
			new { valueVariable = Metric, subtitle = "on {{ vars.system_cpu_name }}", maxValue = 100 },
			registry: registry);

		var caption = registry.GetAll().First(variable => variable.Name == Caption);
		caption.Value = "Apple M4 Max";
		registry.Upsert(caption);

		fixture.Variables.Publish(Caption);

		Assert.That(fixture.Session.DrainPatches(),
			Is.Not.Empty,
			"the subtitle names it, so the card has to follow it even though nothing else does");
	}

	[Test]
	public async Task A_subtitle_naming_the_value_variable_shows_the_new_value_after_one_publish()
	{
		var registry = Registry();
		await using var fixture = new Fixture(
			new { valueVariable = Metric, subtitle = "now {{ vars.system_cpu_usage_percent }}", maxValue = 100 },
			registry: registry);

		SetMetric(registry, 88);
		fixture.Variables.Publish(Metric);

		var written = fixture.Session.DrainPatches()
			.SelectMany(patch => patch.Operations)
			.SelectMany(operation => operation.Properties?.Values ?? [])
			.Select(value => value.GetRawText());

		Assert.That(written, Has.Some.Contains("now 88"));
	}

	[Test]
	public async Task A_change_to_a_variable_the_card_does_not_name_leaves_it_alone()
	{
		await using var fixture = new Fixture(_config);

		fixture.Variables.Publish("something_else");

		Assert.That(fixture.Session.DrainPatches(), Is.Empty);
	}

	[Test]
	public async Task A_sample_that_changed_nothing_puts_nothing_on_the_wire()
	{
		await using var fixture = new Fixture(_config);

		fixture.Window.Push(10, 20, 30);
		fixture.Session.DrainPatches();

		fixture.Window.Raise();

		Assert.That(fixture.Session.DrainPatches(),
			Is.Empty,
			"a flat metric between two ticks must not advance the revision every client tracks");
	}

	[Test]
	public async Task Closing_the_card_lets_go_of_the_window_it_was_sampling()
	{
		var fixture = new Fixture(_config);

		await fixture.DisposeAsync();

		Assert.That(fixture.Window.IsDisposed,
			Is.True,
			"a window nothing holds stops being sampled - a deck left open would otherwise tick forever");
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task A_graph_sampled_from_a_background_thread_while_a_client_interacts_stays_a_healthy_session()
	{
		const int samples = 500;

		await using var fixture = new Fixture(_config);

		var failures = new ConcurrentBag<Exception>();
		var faults = 0;
		fixture.Session.Faulted += (_, _) => Interlocked.Increment(ref faults);

		var start = fixture.Session.BuildTree();

		Assert.That(Contains(start.Root, _graphNodeId),
			Is.True,
			"the dispatching worker below has to name a node this tree actually has");

		var drained = new List<UiPatch>();
		var writersDone = 0;

		RunConcurrently(failures,
			() =>
			{
				for (var index = 0; index < samples; index++)
				{
					fixture.Window.Push(index % 97);
				}

				Interlocked.Increment(ref writersDone);
			},
			// The variable notifier is the session's second refresh path, so this is the reported shape: two
			// threads writing one state while a client is inside the session.
			() =>
			{
				for (var index = 0; index < samples; index++)
				{
					fixture.Variables.Publish(Metric);
				}

				Interlocked.Increment(ref writersDone);
			},
			() =>
			{
				// The graph renders and never handles anything, so the session refuses every one of these -
				// the point is that a client's dispatch takes the session's serialization while the sampling
				// threads write. That the node it names still exists is asserted above, single-threaded.
				for (var index = 0; index < samples; index++)
				{
					fixture.Session.Dispatch(new UiEvent { NodeId = _graphNodeId, Name = "press" });
				}
			},
			() =>
			{
				while (Volatile.Read(ref writersDone) < 2)
				{
					drained.AddRange(fixture.Session.DrainPatches());
				}

				drained.AddRange(fixture.Session.DrainPatches());
			});

		AssertNoWorkerFaulted(failures);

		drained.AddRange(fixture.Session.DrainPatches());

		var patches = drained;
		var replayed = start;

		foreach (var patch in patches)
		{
			var applied = UiTreeApplier.Apply(replayed, patch);

			Assert.That(applied.IsApplied, Is.True, applied.RejectionReason);

			replayed = applied.Tree;
		}

		Assert.Multiple(() =>
		{
			Assert.That(faults, Is.Zero, "the reported crash was an unhandled fault on the sampling thread");
			Assert.That(patches.SelectMany(patch => patch.Operations).Select(operation => operation.Op),
				Has.Some.EqualTo(UiPatchOperations.SetProperties));
		});

		// The deterministic tail: a session left working still turns one distinct sample into one patch.
		fixture.Session.DrainPatches();
		fixture.Window.Push(_distinctSample);

		var tail = fixture.Session.DrainPatches();

		Assert.That(tail, Has.Count.EqualTo(1));
		Assert.That(tail[0].Operations.Select(operation => operation.NodeId), Does.Contain(_chartNodeId));
	}

	[Test]
	[CancelAfter(60_000)]
	public async Task The_real_variable_history_ticking_on_a_background_thread_never_faults_the_session()
	{
		const int ticks = 300;

		var variables = Registry();
		var time = new FakeTimeProvider();
		using var history = new VariableHistory(variables, time);
		using var window = history.Open(Metric, capacity: 60);

		await using var fixture = new Fixture(_config, window);

		var failures = new ConcurrentBag<Exception>();
		var faults = 0;
		fixture.Session.Faulted += (_, _) => Interlocked.Increment(ref faults);

		var start = fixture.Session.BuildTree();

		Assert.That(Contains(start.Root, _graphNodeId),
			Is.True,
			"the dispatching worker below has to name a node this tree actually has");

		var drained = new List<UiPatch>();
		var writersDone = 0;

		// The issue's own stack - Tick -> RaiseChanged -> Refresh -> UiState.Set -> Flush - driven off the
		// test thread, without waiting a real second for it.
		RunConcurrently(failures,
			() =>
			{
				for (var tick = 1; tick <= ticks; tick++)
				{
					SetMetric(variables, tick % 97);
					time.Advance(VariableHistory.SampleInterval);
				}

				Interlocked.Increment(ref writersDone);
			},
			() =>
			{
				for (var index = 0; index < ticks; index++)
				{
					fixture.Variables.Publish(Metric);
				}

				Interlocked.Increment(ref writersDone);
			},
			() =>
			{
				// The graph renders and never handles anything, so the session refuses every one of these -
				// the point is that a client's dispatch takes the session's serialization while the sampling
				// threads write. That the node it names still exists is asserted above, single-threaded.
				for (var index = 0; index < ticks; index++)
				{
					fixture.Session.Dispatch(new UiEvent { NodeId = _graphNodeId, Name = "press" });
				}
			},
			() =>
			{
				while (Volatile.Read(ref writersDone) < 2)
				{
					drained.AddRange(fixture.Session.DrainPatches());
				}

				drained.AddRange(fixture.Session.DrainPatches());
			});

		AssertNoWorkerFaulted(failures);

		drained.AddRange(fixture.Session.DrainPatches());

		var patches = drained;
		var replayed = start;

		foreach (var patch in patches)
		{
			var applied = UiTreeApplier.Apply(replayed, patch);

			Assert.That(applied.IsApplied, Is.True, applied.RejectionReason);

			replayed = applied.Tree;
		}

		Assert.Multiple(() =>
		{
			Assert.That(faults, Is.Zero, "the reported crash was an unhandled fault on the timer thread");
			Assert.That(patches.SelectMany(patch => patch.Operations).Select(operation => operation.Op),
				Has.Some.EqualTo(UiPatchOperations.SetProperties));
		});

		fixture.Session.DrainPatches();
		SetMetric(variables, _distinctSample);
		time.Advance(VariableHistory.SampleInterval);

		var tail = fixture.Session.DrainPatches();

		Assert.That(tail, Has.Count.EqualTo(1));
		Assert.That(tail[0].Operations.Select(operation => operation.NodeId), Does.Contain(_chartNodeId));
	}

	[Test]
	[CancelAfter(60_000)]
	public void Disposing_a_graph_session_while_a_sample_is_in_flight_is_safe()
	{
		const int iterations = 200;

		var failures = new ConcurrentBag<Exception>();
		var faults = 0;

		for (var iteration = 0; iteration < iterations; iteration++)
		{
			var fixture = new Fixture(_config);
			fixture.Session.Faulted += (_, _) => Interlocked.Increment(ref faults);

			var sample = iteration + 1;

			RunConcurrently(failures,
				() => fixture.Window.Push(sample),
				() => fixture.DisposeAsync().AsTask().GetAwaiter().GetResult());

			Assert.That(fixture.Window.IsDisposed, Is.True, $"iteration {iteration}");
		}

		AssertNoWorkerFaulted(failures);

		// Deliberately not asserted: that a post-disposal Raise emits nothing. This session implements no
		// disposal barrier, so that is not a promise it makes - see the answers to the scenario questions.
		Assert.That(faults, Is.Zero, "disposing while a sample lands must not fault the session");
	}

	private static bool Contains(UiNode node, string id)
	{
		if (string.Equals(node.Id, id, StringComparison.Ordinal))
		{
			return true;
		}

		return node.Children.Any(child => Contains(child, id)) ||
			(node.Fallback is not null && Contains(node.Fallback, id));
	}

	private static void SetMetric(VariableRegistry variables, double value)
	{
		var existing = variables.GetAll()
			.First(variable => string.Equals(variable.Name, Metric, StringComparison.Ordinal));

		existing.Value = value.ToString(CultureInfo.InvariantCulture);
		variables.Upsert(existing);
	}

	private sealed class Fixture : IAsyncDisposable
	{
		public Fixture(object data, IVariableHistoryWindow? window = null, VariableRegistry? registry = null)
		{
			var config = Config(data);
			var resolver = Resolver(data, registry);
			_stub = window as StubWindow ?? (window is null ? new StubWindow() : null);
			var sampled = window ?? _stub!;
			Variables = new VariableChangeNotifier();

			var state = new UiState<HistoryGraphViewState>(resolver.Resolve(sampled.Values));
			var view = new UiView(new UiSurface { Kind = UiSurfaceKinds.Widget, SessionMode = UiSessionModes.Shared },
				HistoryGraphWidgetView.Build(state, config));

			Session = new HistoryGraphWidgetSession(view, state, resolver, sampled, Variables, config);

			// The snapshot every client starts from; only what follows it is a patch.
			_ = Session.BuildTree();
			Session.DrainPatches();
		}

		/// <summary>The stub this fixture owns. A test that injected a window of its own drives that one, and
		/// asking for this instead is a defect in the test rather than something a spare stub should hide.
		/// </summary>
		public StubWindow Window
			=> _stub ?? throw new InvalidOperationException("this fixture was built over an injected window");

		private readonly StubWindow? _stub;

		public VariableChangeNotifier Variables { get; }

		public HistoryGraphWidgetSession Session { get; }

		public ValueTask DisposeAsync() => Session.DisposeAsync();
	}

	private sealed class StubWindow : IVariableHistoryWindow
	{
		private readonly List<double> _values = [];

		// The real window locks its buffer; this one has to as well, or a sample read racing a push would
		// fail inside the test's own stub rather than in the session under test.
		private readonly Lock _gate = new();

		public event EventHandler? Changed;

		public bool IsDisposed { get; private set; }

		public IReadOnlyList<double> Values
		{
			get
			{
				lock (_gate)
				{
					return _values.ToArray();
				}
			}
		}

		public void Push(params double[] values)
		{
			lock (_gate)
			{
				_values.AddRange(values);
			}

			Raise();
		}

		public void Raise() => Changed?.Invoke(this, EventArgs.Empty);

		public void Dispose() => IsDisposed = true;
	}
}
