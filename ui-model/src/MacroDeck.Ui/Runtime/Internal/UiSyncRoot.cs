using System.Runtime.ExceptionServices;

namespace MacroDeck.Ui.Runtime.Internal;

/// <summary>
/// The gate one connected component of views and states is serialized by, and the thread-local bookkeeping
/// that keeps a thread from ever holding two of them.
///
/// <para>
/// <b>Two lock classes, one order.</b> A component gate covers everything reachable from a
/// <see cref="UiView" /> - its dirty queues, patches, tree, batch depth and pending work, and every
/// <see cref="UiDependent" />, cell, scope, region and materializer that belongs to it. A state gate covers
/// one <see cref="UiState{T}" />'s own fields. The order is always <i>component then state</i>, and a state
/// gate is never held while a component gate is taken.
/// </para>
///
/// <para>
/// <b>A thread that holds a component gate never acquires a second one.</b> Work aimed at another component
/// is appended to a thread-static list and run at depth 0, after the outermost scope released its gate. That
/// is what makes the design deadlock-free without a global order over components, and it is keyed on whether
/// this thread currently holds a gate rather than on the call site - so work that arrives through a
/// continuation resuming inline on a gate-holding thread is deferred too. <see cref="Merge" /> is the sole
/// two-gate operation: it runs only from depth 0 holding nothing, takes both gates in ascending
/// <see cref="_id" /> order and calls out to nothing while holding them.
/// </para>
///
/// <para>
/// The component itself is a union-find whose "find" is a lock: <see cref="_mergedInto" /> is written once,
/// only while holding this node's own gate, and always points the higher id at the lower - so a terminal
/// holds its component's minimum id, each retry in <see cref="Enter" /> finds a strictly smaller one, and
/// holding the gate while seeing a null forwarding pointer is a stable "I am the terminal". It is a plain
/// field read and written through <see cref="Volatile" /> deliberately: the <c>volatile</c> keyword plus a
/// <c>ref</c> argument is CS0420, which this repository builds as an error.
/// </para>
/// </summary>
internal sealed class UiSyncRoot
{
	/// <summary>How many passes the drain makes before it reports a non-converging plugin rather than
	/// spinning silently.</summary>
	private const int _maxDrainIterations = 1000;

	private static long _nextId;

	[ThreadStatic]
	private static UiSyncRoot? _held;

	[ThreadStatic]
	private static int _depth;

	[ThreadStatic]
	private static bool _draining;

	[ThreadStatic]
	private static List<UiDeferredMerge>? _deferredMerges;

	[ThreadStatic]
	private static List<UiDeferredRead>? _deferredReads;

	[ThreadStatic]
	private static List<IUiTrackedState>? _deferredWrites;

	[ThreadStatic]
	private static List<UiDeferredWork>? _deferredWork;

	[ThreadStatic]
	private static List<UiPendingNotification>? _notifications;

	private readonly Lock _gate = new();
	private readonly long _id = Interlocked.Increment(ref _nextId);
	private UiSyncRoot? _mergedInto;

	/// <summary>Whether <paramref name="root" /> belongs to a component other than the one this thread is
	/// already holding. False at depth 0, where any gate may be taken.</summary>
	internal static bool IsForeign(UiSyncRoot root) => _depth > 0 && !ReferenceEquals(Find(root), _held);

	/// <summary>Whether both roots already belong to one component.</summary>
	internal static bool SameComponent(UiSyncRoot first, UiSyncRoot second)
		=> ReferenceEquals(Find(first), Find(second));

	/// <summary>Merges two components into one. The caller must hold no component gate.</summary>
	internal static void Merge(UiSyncRoot first, UiSyncRoot second)
	{
		while (true)
		{
			var left = Find(first);
			var right = Find(second);

			if (ReferenceEquals(left, right))
			{
				return;
			}

			// Ascending id: the only place two gates are held at once, so the order they are taken in is what
			// keeps two concurrent merges from waiting on each other.
			var (low, high) = left._id < right._id ? (left, right) : (right, left);

			low._gate.Enter();

			try
			{
				if (Volatile.Read(ref low._mergedInto) is not null)
				{
					continue;
				}

				high._gate.Enter();

				try
				{
					if (Volatile.Read(ref high._mergedInto) is not null)
					{
						continue;
					}

					Volatile.Write(ref high._mergedInto, low);

					return;
				}
				finally
				{
					high._gate.Exit();
				}
			}
			finally
			{
				low._gate.Exit();
			}
		}
	}

	/// <summary>Puts two components together without recording anything else - what a state adopts when it
	/// joins a view whose component it is not in yet. Merged at depth 0, because a thread holding a gate may
	/// not take the second one <see cref="Merge" /> needs.</summary>
	internal static void DeferMerge(UiSyncRoot first, UiSyncRoot second)
	{
		if (_depth == 0)
		{
			Merge(first, second);

			return;
		}

		(_deferredMerges ??= []).Add(new UiDeferredMerge(first, second));
	}

	/// <summary>Records that <paramref name="dependent" /> read a state belonging to another component. The
	/// two components are merged and the dependent re-evaluated at depth 0 - a read has to end up in one
	/// component, because the state has to be able to invalidate it.</summary>
	internal static void DeferRead(UiSyncRoot stateRoot, UiSyncRoot viewRoot, UiDependent dependent, UiView view)
		=> (_deferredReads ??= []).Add(new UiDeferredRead(stateRoot, viewRoot, dependent, view));

	/// <summary>Records that a state belonging to another component was written. Only the notification is
	/// deferred - the value is already written - and no merge happens: every dependent and view of that state
	/// already lives in the state's own component.</summary>
	internal static void DeferWrite(IUiTrackedState state)
	{
		var writes = _deferredWrites ??= [];

		if (!writes.Contains(state))
		{
			writes.Add(state);
		}
	}

	/// <summary>Registers asynchronous work against a view in another component, at depth 0. Deferred rather
	/// than skipped: dropping it would let that view's <see cref="UiView.WhenIdleAsync" /> return while the
	/// load it is waiting for is still running.</summary>
	internal static void DeferWork(UiView view, Task work) =>
		(_deferredWork ??= []).Add(new UiDeferredWork(view, work));

	/// <summary>Queues <c>Changed</c> (a <c>null</c> fault) or <c>HandlerFaulted</c> for
	/// <paramref name="view" />, to be raised once this thread is outside every gate. Raised immediately when
	/// it already is - an asynchronous handler's fault lands there.</summary>
	internal static void Raise(UiView view, UiHandlerFaultEventArgs? fault)
	{
		if (_depth == 0)
		{
			view.RaiseNotification(fault);

			return;
		}

		var notifications = _notifications ??= [];

		if (fault is null &&
			notifications.Count > 0 &&
			notifications[^1] is { Fault: null } last &&
			ReferenceEquals(last.View, view))
		{
			notifications[^1] = last with { Count = last.Count + 1 };

			return;
		}

		notifications.Add(new UiPendingNotification(view, fault, 1));
	}

	/// <summary>Takes this component's gate, resolving the terminal first and re-checking it under the gate.
	/// Reentrant on purpose: a dispatch holds the gate across its batch, a handler writes state, and the write
	/// re-enters.</summary>
	internal UiSyncScope Enter()
	{
		while (true)
		{
			var terminal = Find(this);
			terminal._gate.Enter();

			if (Volatile.Read(ref terminal._mergedInto) is null)
			{
				if (_depth++ == 0)
				{
					_held = terminal;
				}

				return new UiSyncScope(terminal);
			}

			// It retired while this thread was waiting for it; its successor has a strictly smaller id.
			terminal._gate.Exit();
		}
	}

	private static UiSyncRoot Find(UiSyncRoot node)
	{
		var current = node;

		while (Volatile.Read(ref current._mergedInto) is { } next)
		{
			current = next;
		}

		return current;
	}

	/// <summary>Runs the work this thread deferred while it held a gate, and then raises what the whole call
	/// produced. Loops, because a foreign component's flush runs value providers that may themselves read or
	/// write across components, and the scopes this drain opens sit at depth 1 and drain nothing.</summary>
	private static void Drain()
	{
		List<Exception>? faults = null;
		_draining = true;

		try
		{
			for (var iteration = 0;; iteration++)
			{
				var merges = _deferredMerges;
				var reads = _deferredReads;
				var work = _deferredWork;
				var writes = _deferredWrites;

				if (merges is null && reads is null && work is null && writes is null)
				{
					break;
				}

				// Cleared before the work runs, so a scope opened below does not see them again.
				_deferredMerges = null;
				_deferredReads = null;
				_deferredWork = null;
				_deferredWrites = null;

				// Everything taken above is discarded with the throw: a drain that will not converge is a
				// defect in the plugin driving it, and finishing this pass would only extend the loop it is
				// stuck in.
				if (iteration >= _maxDrainIterations)
				{
					throw new InvalidOperationException(
						$"A UI component's cross-component work did not settle after {_maxDrainIterations} " +
						"passes. A value provider or handler is writing state that provokes further writes " +
						"without ever converging.");
				}

				if (merges is not null)
				{
					foreach (var merge in merges)
					{
						Merge(merge.First, merge.Second);
					}
				}

				DrainReads(reads, ref faults);
				DrainWork(work, ref faults);
				DrainWrites(writes, ref faults);
			}
		}
		finally
		{
			_draining = false;
		}

		Rethrow(faults);
	}

	private static void DrainReads(List<UiDeferredRead>? reads, ref List<Exception>? faults)
	{
		if (reads is null)
		{
			return;
		}

		foreach (var read in reads)
		{
			Merge(read.StateRoot, read.ViewRoot);
		}

		foreach (var read in reads)
		{
			try
			{
				// A full flush rather than a bare Invalidate: the dependent has to re-record the dependency it
				// could not record while the components were separate, and only re-evaluating does that.
				using (read.ViewRoot.Enter())
				{
					read.Dependent.Invalidate();
					read.View.OnStateChanged();
				}
			}
#pragma warning disable CA1031 // One item's fault must not swallow the rest of the drain; it is rethrown below.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				(faults ??= []).Add(exception);
			}
		}
	}

	private static void DrainWork(List<UiDeferredWork>? work, ref List<Exception>? faults)
	{
		if (work is null)
		{
			return;
		}

		foreach (var pending in work)
		{
			try
			{
				pending.View.RegisterPendingWork(pending.Work);
			}
#pragma warning disable CA1031 // See DrainReads.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				(faults ??= []).Add(exception);
			}
		}
	}

	private static void DrainWrites(List<IUiTrackedState>? writes, ref List<Exception>? faults)
	{
		if (writes is null)
		{
			return;
		}

		foreach (var write in writes)
		{
			try
			{
				write.NotifyDeferredWrite();
			}
#pragma warning disable CA1031 // See DrainReads.
			catch (Exception exception)
#pragma warning restore CA1031
			{
				(faults ??= []).Add(exception);
			}
		}
	}

	private static void RaisePending()
	{
		List<Exception>? faults = null;

		while (_notifications is { Count: > 0 } pending)
		{
			// Taken before the raises, so a handler that writes state raises its own notifications rather than
			// having them run twice from here.
			_notifications = null;

			foreach (var notification in pending)
			{
				for (var raise = 0; raise < notification.Count; raise++)
				{
					try
					{
						notification.View.RaiseNotification(notification.Fault);
					}
#pragma warning disable CA1031 // A subscriber is host code; one that throws must not swallow the remaining raises.
					catch (Exception exception)
#pragma warning restore CA1031
					{
						(faults ??= []).Add(exception);
					}
				}
			}
		}

		Rethrow(faults);
	}

	private static void Rethrow(List<Exception>? faults)
	{
		if (faults is null)
		{
			return;
		}

		if (faults.Count == 1)
		{
			ExceptionDispatchInfo.Throw(faults[0]);
		}

		throw new AggregateException(faults);
	}

	private void Exit()
	{
		var depth = --_depth;

		if (depth == 0)
		{
			_held = null;
		}

		_gate.Exit();

		// Both run outside every gate: the deferred work takes gates of its own, and a Changed subscriber runs
		// host code that may write, drain or dispatch straight back in.
		if (depth != 0 || _draining)
		{
			return;
		}

		try
		{
			Drain();
		}
		finally
		{
			RaisePending();
		}
	}

	/// <summary>Two components that have to become one, with nothing to re-evaluate afterwards.</summary>
	private readonly record struct UiDeferredMerge(UiSyncRoot First, UiSyncRoot Second);

	/// <summary>A read that crossed components: what to merge, and what to re-evaluate once merged.</summary>
	private readonly record struct UiDeferredRead(
		UiSyncRoot StateRoot,
		UiSyncRoot ViewRoot,
		UiDependent Dependent,
		UiView View);

	/// <summary>Asynchronous work waiting to be registered against a view in another component.</summary>
	private readonly record struct UiDeferredWork(UiView View, Task Work);

	/// <summary>A queued raise. <c>Fault</c> is <c>null</c> for <c>Changed</c>, whose consecutive raises for
	/// one view are counted rather than listed.</summary>
	private readonly record struct UiPendingNotification(UiView View, UiHandlerFaultEventArgs? Fault, int Count);

	/// <summary>The handle <see cref="Enter" /> returns, releasing the terminal it actually took.</summary>
	internal readonly struct UiSyncScope : IDisposable
	{
		private readonly UiSyncRoot _terminal;

		internal UiSyncScope(UiSyncRoot terminal) => _terminal = terminal;

		/// <summary>Releases the gate and, at depth 0, runs whatever this call deferred.</summary>
		public void Dispose() => _terminal.Exit();
	}
}
