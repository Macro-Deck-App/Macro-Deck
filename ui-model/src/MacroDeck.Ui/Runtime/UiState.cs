using MacroDeck.Ui.Runtime.Internal;

namespace MacroDeck.Ui.Runtime;

/// <summary>
/// A piece of mutable state a view can be built from. Reading it inside a build records a dependency;
/// writing it invalidates exactly the property cells and structural scopes that read it, and nothing else.
///
/// <para>
/// <b>Attachment is lazy.</b> A state attaches to a view the first time that view reads it during a build.
/// A state written before any view has read it - a step's state set before the scope that renders that step
/// has ever materialized - simply updates in place: there is nothing to invalidate and nothing to flush,
/// which is exactly right rather than a special case.
/// </para>
///
/// <para>
/// <b>An idle write is free.</b> A value that compares equal under this state's comparer returns
/// immediately: no dependent is marked, no flush runs, no revision advances and no <c>Changed</c> fires.
/// That is a correctness requirement, not an optimization - a patch that carries no operations, or one that
/// advances the revision without changing anything, is inapplicable per
/// <see cref="Model.Patches.UiPatchSequencing" /> and would desynchronise every renderer's revision counter.
/// </para>
///
/// <para>
/// <b>Any thread may read or write.</b> The runtime serializes a state and the views reading it, so a write
/// from a timer, a continuation or a background service is safe against a dispatch on another thread - see
/// <see cref="UiView" />. Two things follow from the flush running on the writing thread. A write that
/// reaches a view whose other states are being written by another thread may run that view's flush - value
/// providers included - on your thread before <see cref="Set" /> returns, while you still hold whatever
/// locks you called under; so a value provider must not block, and neither may a lock you hold across a
/// write. And between such a write returning and that view's flush, <see cref="Peek" /> and
/// <see cref="Value" /> already answer with the new value while the view's tree still shows the old one -
/// the same window <see cref="UiView.Batch" /> opens deliberately, closed before the writing call returns.
/// </para>
/// </summary>
public sealed class UiState<T> : IUiTrackedState
{
	private readonly IEqualityComparer<T> _comparer;
	private readonly List<UiDependent> _dependents = [];
	private readonly List<UiView> _views = [];
	private readonly Lock _gate = new();
	private UiSyncRoot? _sync;
	private T _value;

	/// <summary>Creates a state holding <paramref name="initialValue" />, comparing values with
	/// <see cref="EqualityComparer{T}.Default" />.</summary>
	public UiState(T initialValue)
		: this(initialValue, EqualityComparer<T>.Default)
	{
	}

	/// <summary>Creates a state holding <paramref name="initialValue" />, using
	/// <paramref name="comparer" /> to decide whether a write changes anything - for example
	/// <see cref="StringComparer.OrdinalIgnoreCase" /> for a value whose case carries no meaning.</summary>
	public UiState(T initialValue, IEqualityComparer<T> comparer)
	{
		ArgumentNullException.ThrowIfNull(comparer);

		_comparer = comparer;
		_value = initialValue;
	}

	/// <summary>
	/// The current value. The getter <b>records a dependency</b> when it runs inside a build, which is how a
	/// value provider, a condition or a template declares what it reads without the author wiring anything
	/// up. Use <see cref="Peek" /> to read without recording one. The setter is
	/// <see cref="Set" />.
	/// </summary>
	public T Value
	{
		get
		{
			UiTracking.Register(this);

			return Peek();
		}
		set => Set(value);
	}

	/// <summary>
	/// Writes <paramref name="value" />. Returns without doing anything when it compares equal to the
	/// current value under this state's comparer. Otherwise marks every dependent that read this state and
	/// flushes each attached view - unless a batch is open, in which case the flush waits for it to close.
	/// </summary>
	public void Set(T value)
	{
		UiSyncRoot root;

		lock (_gate)
		{
			if (_sync is not { } attached)
			{
				// Nothing has read this state, so there is nothing to invalidate and no component to enter.
				if (!_comparer.Equals(_value, value))
				{
					_value = value;
				}

				return;
			}

			root = attached;
		}

		if (UiSyncRoot.IsForeign(root))
		{
			// The value lands now and only the notification waits: every dependent and view of this state lives
			// in this state's own component, so entering a second gate here is never necessary.
			lock (_gate)
			{
				if (_comparer.Equals(_value, value))
				{
					return;
				}

				_value = value;
			}

			UiSyncRoot.DeferWrite(this);

			return;
		}

		using (root.Enter())
		{
			UiDependent[] dependents;
			UiView[] views;

			lock (_gate)
			{
				if (_comparer.Equals(_value, value))
				{
					return;
				}

				_value = value;

				// Snapshots, because a flush re-evaluates cells, and a cell that re-evaluates drops and
				// re-records its dependencies - mutating both lists while they are being walked. Taken under
				// this state's own gate and used after it is released, so no component gate is ever entered
				// from underneath it.
				dependents = [.. _dependents];
				views = [.. _views];
			}

			Notify(dependents, views);
		}
	}

	/// <summary>
	/// Reads the current value <b>without</b> recording a dependency. Exists because a writer needs to see
	/// the current value without becoming a reader of it: an event handler that reads state to decide what to
	/// write, or a test asserting what a dispatch stored, would otherwise subscribe the cell that happens to
	/// be evaluating and invalidate it forever after.
	/// </summary>
	public T Peek()
	{
		lock (_gate)
		{
			return _value;
		}
	}

	/// <summary>
	/// Called with each view this state newly attaches to. <see cref="UiAsyncState{T}" /> uses it to hand a
	/// load that is already in flight to a view that has only just started reading it: a load started before
	/// anything rendered would otherwise be invisible to <see cref="UiView.WhenIdleAsync" />, and a test would
	/// settle instantly on work that has not happened.
	/// </summary>
	internal Action<UiView>? ViewAttached { get; set; }

	/// <summary>Puts this state in <paramref name="root" />'s component, so a caller that has to touch several
	/// related states while holding one gate only ever needs that one. Adopts the root when nothing has read
	/// this state yet, does nothing when the two are already one component, and otherwise leaves the merge to
	/// the depth-0 drain - a thread holding a gate may not take the second one a merge needs.</summary>
	internal void AttachTo(UiSyncRoot root)
	{
		UiSyncRoot existing;

		lock (_gate)
		{
			if (_sync is not { } attached)
			{
				_sync = root;

				return;
			}

			existing = attached;
		}

		// Outside this state's gate: a merge takes component gates, which must never be taken from underneath
		// a state gate.
		if (!UiSyncRoot.SameComponent(existing, root))
		{
			UiSyncRoot.DeferMerge(existing, root);
		}
	}

	/// <summary>Adds every view currently reading this state to <paramref name="into" />, without
	/// duplicates. Takes this state's gate only - a caller that goes on to enter those views' component gates
	/// must have released it first.</summary>
	internal void CollectViews(List<UiView> into)
	{
		lock (_gate)
		{
			foreach (var view in _views)
			{
				if (!into.Contains(view))
				{
					into.Add(view);
				}
			}
		}
	}

	private static void Notify(UiDependent[] dependents, UiView[] views)
	{
		foreach (var dependent in dependents)
		{
			dependent.Invalidate();
		}

		foreach (var view in views)
		{
			view.OnStateChanged();
		}
	}

	bool IUiTrackedState.Observe(UiDependent dependent, UiView view)
	{
		var attached = false;

		lock (_gate)
		{
			if (_sync is not { } existing)
			{
				_sync = view.SyncRoot;
			}
			else if (!UiSyncRoot.SameComponent(existing, view.SyncRoot))
			{
				// The reader is in another component. Nothing is recorded here: the two are merged at depth 0
				// and the dependent re-evaluates then, which is what records the dependency for real.
				UiSyncRoot.DeferRead(existing, view.SyncRoot, dependent, view);

				return false;
			}

			if (!_dependents.Contains(dependent))
			{
				_dependents.Add(dependent);
			}

			if (!_views.Contains(view))
			{
				_views.Add(view);
				attached = true;
			}
		}

		if (attached)
		{
			// Outside this state's gate: the handler enters the view's component gate, which is already held
			// here and must never be taken from underneath a state gate.
			ViewAttached?.Invoke(view);
		}

		return true;
	}

	void IUiTrackedState.Unobserve(UiDependent dependent)
	{
		lock (_gate)
		{
			_dependents.Remove(dependent);
		}
	}

	void IUiTrackedState.NotifyDeferredWrite()
	{
		UiSyncRoot root;

		lock (_gate)
		{
			if (_sync is not { } attached)
			{
				return;
			}

			root = attached;
		}

		using (root.Enter())
		{
			UiDependent[] dependents;
			UiView[] views;

			lock (_gate)
			{
				dependents = [.. _dependents];
				views = [.. _views];
			}

			Notify(dependents, views);
		}
	}
}
