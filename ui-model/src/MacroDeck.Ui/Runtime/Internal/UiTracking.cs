namespace MacroDeck.Ui.Runtime.Internal;

/// <summary>
/// The ambient build scope dependency tracking runs through: while a cell or a structural scope is being
/// evaluated, it is the <i>current</i> dependent, and every reactive state read during that evaluation
/// records itself against it.
///
/// <para>
/// The current dependent is <see cref="ThreadStaticAttribute" />, and stays so now that the runtime is
/// thread-safe: <see cref="UiSyncRoot" /> serializes a component, so at most one build per component runs at
/// a time, but two components build concurrently on two threads and each needs its own ambient scope. What
/// makes per-thread storage sufficient is that a build is required to be <b>synchronous and pure</b>: a
/// value provider, a <see cref="Dsl.UiWhen.Condition" />, a <see cref="Dsl.UiWhen.Content" /> and a
/// <see cref="Dsl.UiRepeat{TItem}.Template" /> must return on the calling thread without awaiting, so every
/// <see cref="Push" /> and its disposal sit in one synchronous region on one thread. A provider that awaits
/// resumes on another thread with no ambient scope, so the state it reads after the await is silently
/// untracked and its cell never invalidates - asynchronous work belongs in a state a synchronous provider
/// reads, never inside the provider.
/// </para>
/// </summary>
internal static class UiTracking
{
	[ThreadStatic]
	private static UiDependent? _current;

	/// <summary>Records <paramref name="state" /> as a dependency of whatever is currently being evaluated.
	/// A no-op outside a build scope, which is what makes a read through <c>Peek</c> - or a read from
	/// ordinary application code - non-tracking.</summary>
	internal static void Register(IUiTrackedState state) => _current?.RecordDependency(state);

	/// <summary>Makes <paramref name="dependent" /> the current dependent until the returned scope is
	/// disposed. A <c>null</c> dependent installs "no tracking", which is what an untracked one-shot build
	/// (<see cref="UiViewBuilder.Build" />) uses, and also stops an inner build from attributing its reads to
	/// an outer scope.</summary>
	internal static Scope Push(UiDependent? dependent) => new(dependent);

	/// <summary>Restores the previously current dependent on disposal.</summary>
	internal readonly struct Scope : IDisposable
	{
		private readonly UiDependent? _previous;

		internal Scope(UiDependent? dependent)
		{
			_previous = _current;
			_current = dependent;
		}

		/// <summary>Restores the enclosing scope's dependent.</summary>
		public void Dispose() => _current = _previous;
	}
}

/// <summary>A reactive state a <see cref="UiDependent" /> can observe. Implemented by
/// <see cref="UiState{T}" /> explicitly, so none of this appears on its public surface.</summary>
internal interface IUiTrackedState
{
	/// <summary>Registers <paramref name="dependent" /> as reading this state, and attaches this state to
	/// <paramref name="view" /> so a later write knows which view to flush. Attachment on first read is what
	/// lets a state be written before any view has materialized the scope that reads it: it simply updates in
	/// place. Returns <c>false</c> when the state belongs to another component and nothing was recorded: the
	/// read is deferred, the two components are merged at depth 0 and the dependent re-evaluates then, so the
	/// caller must not remember a dependency it does not have.</summary>
	bool Observe(UiDependent dependent, UiView view);

	/// <summary>Marks and flushes this state's dependents for a write that already landed, which a
	/// cross-component write defers to depth 0.</summary>
	void NotifyDeferredWrite();

	/// <summary>Drops <paramref name="dependent" />, called before it re-evaluates so a provider whose
	/// dependencies changed does not keep the old ones.</summary>
	void Unobserve(UiDependent dependent);
}

/// <summary>
/// Something whose value is derived from reactive state and therefore has to be re-evaluated when that state
/// changes: a property cell or a structural scope. Owns the reverse index's consumer half: the set of states it
/// currently reads.
///
/// <para>
/// Every field here belongs to its view's component gate - see <see cref="UiSyncRoot" /> - and is only ever
/// touched with that gate held.
/// </para>
/// </summary>
internal abstract class UiDependent
{
	private readonly List<IUiTrackedState> _dependencies = [];
	private UiView? _view;

	/// <summary>True once this dependent's node is no longer in the tree. A released dependent neither
	/// subscribes to anything nor contributes an operation: the node it fed does not exist any more, so a
	/// pending invalidation of it describes a patch no renderer could apply.</summary>
	internal bool IsReleased { get; private set; }

	/// <summary>The view this dependent belongs to, or <c>null</c> for an untracked one-shot build. A null
	/// view means nothing is ever recorded, so no state can retain a view that was never handed out.
	/// </summary>
	internal UiView? View => _view;

	/// <summary>Binds this dependent to <paramref name="view" />, which the materializer does before the
	/// first evaluation.</summary>
	internal void BindTo(UiView? view) => _view = view;

	/// <summary>Records <paramref name="state" /> as read by this dependent, once per distinct state.
	/// </summary>
	internal void RecordDependency(IUiTrackedState state)
	{
		if (_view is null || _dependencies.Contains(state))
		{
			return;
		}

		// Remembered only once the state actually recorded it, and not before the call: a cross-component read
		// records nothing, and the re-evaluation its merge schedules has to reach Observe again to record it
		// for real. Adding it up front instead makes this guard skip that second call - permanently for a
		// structural scope, which deliberately never clears its dependencies - and the dependent then holds a
		// state that does not hold it back, so writes stop reaching it and ViewAttached never fires.
		if (state.Observe(this, _view))
		{
			_dependencies.Add(state);
		}
	}

	/// <summary>Detaches this dependent from every state it currently reads. Called before a
	/// re-evaluation, and when the view discards this dependent - a dependent left registered would keep
	/// being invalidated and would re-evaluate into a node nobody holds any more.</summary>
	internal void ClearDependencies()
	{
		foreach (var state in _dependencies)
		{
			state.Unobserve(this);
		}

		_dependencies.Clear();
	}

	/// <summary>Detaches this dependent for good, because the node it fed left the tree. Distinct from
	/// <see cref="ClearDependencies" />, which a live dependent calls before re-recording what it reads.
	/// </summary>
	internal void Release()
	{
		ClearDependencies();
		IsReleased = true;
	}

	/// <summary>Called by a state that changed. Marks this dependent as needing re-evaluation; it never
	/// evaluates anything itself, because a write inside a batch must not touch the tree.</summary>
	internal abstract void Invalidate();
}
