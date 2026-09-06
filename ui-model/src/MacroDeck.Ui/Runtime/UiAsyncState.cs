using MacroDeck.Ui.Runtime.Internal;

namespace MacroDeck.Ui.Runtime;

/// <summary>
/// State whose value is produced by asynchronous work, exposed as three tracked reads - the value, whether a
/// load is running, and the fault the last one produced. Reading any of them inside a build records a
/// dependency exactly as <see cref="UiState{T}" /> does, so a node showing a spinner while a list loads needs
/// no wiring beyond reading <see cref="IsLoading" />.
///
/// <para>
/// It knows nothing about options, filters or configuration: a load is a <c>Func</c> and a value is a
/// <typeparamref name="T" />. The query-carrying variant belongs to the configuration profile, which composes
/// this rather than extending it.
/// </para>
///
/// <para>
/// <b>A load never throws at the caller.</b> A faulting loader lands on <see cref="Error" />. A plugin whose
/// service is down has to leave a usable surface behind - an error the user can read and retry from - rather
/// than an exception escaping a render or a dispatch, or work that never finishes and hangs the dialog.
/// </para>
///
/// <para>
/// <b>Last write wins.</b> <see cref="Reload" /> cancels the load already in flight and ignores whatever it
/// eventually produces, so a response arriving after the request that superseded it cannot overwrite the newer
/// answer. Nothing but reference identity decides which load is current, so the ordering does not depend on
/// how long either took.
/// </para>
///
/// <para>
/// <b>No clock and no scheduler.</b> A load starts when something starts it - typically a node's
/// <c>reload</c>, <c>open</c> or <c>filter</c> handler. Nothing here polls, debounces, expires or retries.
/// </para>
///
/// <para>
/// <b>Any thread may reload.</b> Concurrent reloads settle on the one started last, and the load that lost
/// is cancelled exactly once - see <see cref="UiState{T}" /> for what the runtime guarantees the writes
/// themselves.
/// </para>
/// </summary>
public sealed class UiAsyncState<T>
{
	private readonly Func<CancellationToken, Task<T>> _load;
	private readonly UiState<Exception?> _error = new(null);
	private readonly UiState<bool> _isLoading = new(false);
	private readonly UiState<T> _value;

	// The innermost lock in the runtime's order - component gate, then state gate, then this - so it is never
	// held across a state write, a batch, or the cancellation below, all of which take one of the outer two.
	private readonly Lock _sync = new();
	private UiLoad? _current;
	private Task? _inFlight;

	/// <summary>Creates a state that produces its value with <paramref name="load" />, holding
	/// <paramref name="initialValue" /> until the first load succeeds. No load is started - see this type's
	/// remarks.</summary>
	public UiAsyncState(Func<CancellationToken, Task<T>> load, T initialValue)
	{
		ArgumentNullException.ThrowIfNull(load);

		_load = load;
		_value = new UiState<T>(initialValue);

		_value.ViewAttached = OnViewAttached;
		_isLoading.ViewAttached = OnViewAttached;
		_error.ViewAttached = OnViewAttached;
	}

	/// <summary>The value the last successful load produced, or the initial value. A tracked read.</summary>
	public T Value => _value.Value;

	/// <summary>Whether a load is running. A tracked read, and the natural gate for a busy indicator.
	/// </summary>
	public bool IsLoading => _isLoading.Value;

	/// <summary>The fault the last load produced, or <c>null</c>. A tracked read, cleared when the next load
	/// starts.</summary>
	public Exception? Error => _error.Value;

	/// <summary>Reads the current value without recording a dependency - see
	/// <see cref="UiState{T}.Peek" />.</summary>
	public T Peek() => _value.Peek();

	/// <summary>
	/// Starts a load, cancelling the one in flight. Returns as soon as the work is started: the caller is a
	/// synchronous event dispatch or a synchronous build, and neither may block on a network call.
	/// </summary>
	public void Reload()
	{
		var current = new UiLoad();
		UiLoad? previous;

		lock (_sync)
		{
			previous = _current;
			_current = current;
		}

		// Outside the lock, because a cancellation registration runs inline and can resume RunAsync's
		// continuation on this thread - which writes state, and would take a component gate from underneath
		// this one.
		previous?.Cancel();

		using (BatchAttachedViews())
		{
			_error.Value = null;
			_isLoading.Value = true;
		}

		var task = RunAsync(current);
		var registered = false;

		lock (_sync)
		{
			if (ReferenceEquals(_current, current))
			{
				_inFlight = task;
				registered = true;
			}
		}

		if (!registered)
		{
			return;
		}

		var views = new List<UiView>();
		CollectViews(views);

		foreach (var view in views)
		{
			view.RegisterPendingWork(task);
		}
	}

	private async Task RunAsync(UiLoad source)
	{
		try
		{
			var value = await _load(source.Token).ConfigureAwait(false);

			if (!IsCurrent(source))
			{
				return;
			}

			using (BatchAttachedViews())
			{
				_value.Value = value;
				_isLoading.Value = false;
			}
		}
		catch (OperationCanceledException)
		{
			Settle(source, fault: null);
		}
#pragma warning disable CA1031 // Any loader fault becomes Error by contract - see this type's remarks.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			Settle(source, exception);
		}
		finally
		{
			lock (_sync)
			{
				if (ReferenceEquals(_current, source))
				{
					_current = null;
					_inFlight = null;
				}
			}

			source.Dispose();
		}
	}

	private bool IsCurrent(UiLoad source)
	{
		lock (_sync)
		{
			return ReferenceEquals(_current, source);
		}
	}

	/// <summary>Records how a load ended, ignoring one that is no longer the current load.</summary>
	private void Settle(UiLoad source, Exception? fault)
	{
		if (!IsCurrent(source))
		{
			return;
		}

		using (BatchAttachedViews())
		{
			if (fault is not null)
			{
				_error.Value = fault;
			}

			_isLoading.Value = false;
		}
	}

	/// <summary>Hands a load that is already running to a view that has only just started reading this state.
	/// </summary>
	private void OnViewAttached(UiView view)
	{
		// All three states join the attaching view's component, so BatchAttachedViews only ever opens batches
		// on views this thread's own gate already covers. Without it the three can end up in two components,
		// and opening a batch on each would mean holding two component gates at once - the one thing the
		// runtime's lock protocol forbids.
		_value.AttachTo(view.SyncRoot);
		_isLoading.AttachTo(view.SyncRoot);
		_error.AttachTo(view.SyncRoot);

		Task? task;

		lock (_sync)
		{
			task = _inFlight;
		}

		if (task is not null && !task.IsCompleted)
		{
			view.RegisterPendingWork(task);
		}
	}

	private void CollectViews(List<UiView> into)
	{
		_value.CollectViews(into);
		_isLoading.CollectViews(into);
		_error.CollectViews(into);
	}

	/// <summary>
	/// Opens a batch on every view reading this state, so the two or three writes one load transition performs
	/// land in one patch and one revision. Without it a finished load would emit the value and the cleared
	/// loading flag as separate revisions, exposing a tree that is done loading but still holds the old value.
	/// </summary>
	private UiCompositeScope BatchAttachedViews()
	{
		// The views are collected first and under nothing but the states' own gates: opening a batch enters a
		// component gate, which must never be taken from underneath a state gate.
		var views = new List<UiView>();
		CollectViews(views);

		var scopes = new List<IDisposable>(views.Count);

		foreach (var view in views)
		{
			// Opening a batch enters that view's gate, and a thread already holding one never takes a second.
			// After OnViewAttached every view reading this state is in one component, so this only triggers
			// while an attach-time merge is still pending; those writes then reach the view as deferred
			// notifications at depth 0 instead, costing it an extra patch rather than risking a lock cycle.
			if (UiSyncRoot.IsForeign(view.SyncRoot))
			{
				continue;
			}

			scopes.Add(view.Batch());
		}

		return new UiCompositeScope(scopes);
	}

	/// <summary>
	/// One load's cancellation source, reference counted so it is disposed exactly once and never while
	/// somebody is still using it.
	///
	/// <para>
	/// The runner holds the initial reference and drops it when the load has ended; a reload cancelling this
	/// load takes one for the duration of the cancel, and gets nothing if the load already finished. That is
	/// what keeps a <see cref="CancellationTokenSource.Cancel()" /> on one thread from meeting a
	/// <see cref="CancellationTokenSource.Dispose()" /> on another, which surfaced as an
	/// <see cref="ObjectDisposedException" /> on <c>Error</c>. The token is captured up front for the same
	/// reason: reading it from a disposed source throws.
	/// </para>
	/// </summary>
	private sealed class UiLoad : IDisposable
	{
		private readonly CancellationTokenSource _source = new();

		// Last one out disposes, rather than the canceller: a reload that supersedes this load can be racing a
		// second one that superseded it in turn, and the source it hands to RunAsync would then already be gone.
		private int _users = 1;

		internal UiLoad() => Token = _source.Token;

		/// <summary>The token this load's work observes.</summary>
		internal CancellationToken Token { get; }

		/// <summary>Drops the runner's reference; the last one out disposes.</summary>
		public void Dispose() => Release();

		/// <summary>Cancels this load, unless it has already ended - in which case there is nothing to cancel
		/// and nothing to keep alive.</summary>
		internal void Cancel()
		{
			if (!TryAcquire())
			{
				return;
			}

			try
			{
				_source.Cancel();
			}
			finally
			{
				Release();
			}
		}

		private bool TryAcquire()
		{
			var users = Volatile.Read(ref _users);

			while (users > 0)
			{
				var seen = Interlocked.CompareExchange(ref _users, users + 1, users);

				if (seen == users)
				{
					return true;
				}

				users = seen;
			}

			return false;
		}

		private void Release()
		{
			if (Interlocked.Decrement(ref _users) == 0)
			{
				_source.Dispose();
			}
		}
	}

	/// <summary>Closes several batches as one. Idempotent, like the batch scopes it holds.</summary>
	private sealed class UiCompositeScope : IDisposable
	{
		private List<IDisposable>? _scopes;

		internal UiCompositeScope(List<IDisposable> scopes) => _scopes = scopes;

		public void Dispose()
		{
			var scopes = _scopes;

			if (scopes is null)
			{
				return;
			}

			_scopes = null;

			foreach (var scope in scopes)
			{
				scope.Dispose();
			}
		}
	}
}
