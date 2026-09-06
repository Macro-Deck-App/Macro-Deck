using System.Text.Json;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime.Internal;

namespace MacroDeck.Ui.Runtime;

/// <summary>
/// A live view: an authored <see cref="UiElement" /> tree materialized once, plus the patches every
/// subsequent state change compiles to. The tree is built at revision 0 and each flush that changes something
/// advances the revision by exactly one and enqueues one <see cref="UiPatch" /> for
/// <see cref="DrainPatches" />.
///
/// <para>
/// <b>What a flush guarantees.</b> A property change re-evaluates only the cells that actually read the state
/// that changed, emits one <c>set-properties</c> per affected node listing only the keys whose value
/// changed, and rebuilds only the spine from the root to those nodes - every untouched subtree stays the
/// same <see cref="UiNode" /> instance, so nothing outside the spine is re-allocated or re-serialized. A
/// flush that finds nothing changed enqueues nothing and does not advance the revision: an operation-less
/// patch is inapplicable per <see cref="UiPatchSequencing" />, so emitting one would be a bug rather than a
/// harmless no-op.
/// </para>
///
/// <para>
/// <b>What a structural change guarantees.</b> A <see cref="UiWhen" /> that flipped or a
/// <see cref="UiRepeat{TItem}" /> whose item list was replaced re-materializes only the run of its parent's
/// child list that it owns, and diffs that run by id: one <c>remove-node</c> for the top-most node of each
/// removed subtree, <c>move-node</c> for a node that changed position, <c>insert-node</c> for new content at
/// its <i>rendered</i> index, <c>replace-node</c> for a node whose type changed, and one <c>set-properties</c>
/// per node whose properties differ. A node that survives keeps its id, and with it the focus and in-flight
/// edits stable identity exists to preserve. A condition re-evaluated to the boolean it already had, or an item
/// list that is still the same instance, emits nothing at all.
/// </para>
///
/// <para>
/// <b>What thread any of it happens on.</b> Any thread may build, read, write state into, dispatch to and
/// drain a view. The runtime serializes a view together with every state it reads and every other view those
/// states reach, so a write from a timer or a continuation cannot interleave with a dispatch on another
/// thread; views that share no state proceed in parallel, so one plugin's slow value provider cannot stall
/// another session. What runs under that serialization is plugin code - value providers, conditions,
/// templates, synchronous handlers and binding setters - so none of it may block on another thread's work.
/// <see cref="Changed" /> and <see cref="HandlerFaulted" /> are deliberately raised outside it, on the thread
/// that did the work rather than on any pump, once the patch is already queued: a subscriber may read
/// <see cref="Tree" />, call <see cref="DrainPatches" />, write state and dispatch again.
/// </para>
/// </summary>
public sealed class UiView
{
	private readonly UiSyncRoot _sync = new();
	private readonly List<UiPropertyCell> _dirtyCells = [];
	private readonly List<UiStructuralScope> _dirtyScopes = [];
	private readonly List<UiPatch> _patches = [];
	private readonly List<Task> _pendingWork = [];
	private readonly UiStructuralReconciler _reconciler;
	private UiMaterializedNode _rootNode;
	private UiTree _tree;
	private int _batchDepth;
	private bool _built;
	private bool _flushing;

	/// <summary>Materializes <paramref name="root" /> for <paramref name="surface" /> at revision 0. Throws
	/// <see cref="UiViewException" /> when the tree violates an identity rule - see
	/// <see cref="UiViewBuilder" />.</summary>
	public UiView(UiSurface surface, UiElement root)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(root);

		Surface = surface;
		_reconciler = new UiStructuralReconciler(this);

		var materializer = new UiElementMaterializer(this);

		// The gate covers the failure path too: the release below detaches cells from states another thread may
		// be writing at that moment.
		using (_sync.Enter())
		{
			try
			{
				_rootNode = materializer.MaterializeRoot(root);
			}
			catch
			{
				// A half-built view must not leave cells subscribed to states that outlive it.
				foreach (var dependent in materializer.Dependents)
				{
					dependent.Release();
				}

				throw;
			}

			_tree = new UiTree { Revision = 0, Surface = surface, Root = _rootNode.Node };
			_built = true;

			// A provider that wrote state while the tree was being materialized - a value read that starts the
			// load it is waiting for - marked its cells against a view that did not exist yet, so nothing could
			// flush them. They belong to revision 1, not to a queue nobody drains.
			if (_dirtyCells.Count > 0 || _dirtyScopes.Count > 0)
			{
				Flush();
			}
		}
	}

	/// <summary>
	/// Raised when an event handler faulted, synchronously from <see cref="Dispatch" /> for a synchronous
	/// handler and when the task completes for an asynchronous one. One handler getting something wrong must
	/// not take the surface down, and an asynchronous fault must not go unobserved either - so the fault is
	/// reported here rather than thrown at whoever dispatched the event.
	/// </summary>
	public event EventHandler<UiHandlerFaultEventArgs>? HandlerFaulted;

	/// <summary>Raised after a flush enqueued a patch. Deliberately payload-free: it says "there is something
	/// to drain", and the patches themselves come from <see cref="DrainPatches" />, so a handler cannot
	/// consume a patch the next handler then misses.</summary>
	public event EventHandler? Changed;

	/// <summary>The surface this view renders for.</summary>
	public UiSurface Surface { get; }

	/// <summary>The revision <see cref="Tree" /> is at. Advances by exactly one per enqueued patch.</summary>
	public int Revision
	{
		get
		{
			using (_sync.Enter())
			{
				return _tree.Revision;
			}
		}
	}

	/// <summary>The current tree. Inside a <see cref="Batch" /> this is still the pre-batch tree: a batch's
	/// changes become visible when it closes, so no intermediate tree the author never intended is ever
	/// observable. Read together with <see cref="Revision" /> under the same serialization, so the two are
	/// never observed one flush apart.</summary>
	public UiTree Tree
	{
		get
		{
			using (_sync.Enter())
			{
				return _tree;
			}
		}
	}

	/// <summary>
	/// How many registered pieces of asynchronous work have not finished - what <see cref="WhenIdleAsync" /> is
	/// still waiting for. A diagnostic, so a test host that gave up waiting can say what it gave up on rather
	/// than only how long it waited; nothing about the tree or the patches depends on it, and the count says
	/// nothing about what the work is.
	/// </summary>
	public int PendingWorkCount
	{
		get
		{
			using (_sync.Enter())
			{
				_pendingWork.RemoveAll(work => work.IsCompleted);

				return _pendingWork.Count;
			}
		}
	}

	/// <summary>Takes every patch enqueued since the last call and clears the queue. Safe from any thread and
	/// from inside a <see cref="Changed" /> handler: two concurrent calls between them return exactly what was
	/// queued, neither losing nor duplicating a patch.</summary>
	public IReadOnlyList<UiPatch> DrainPatches()
	{
		using (_sync.Enter())
		{
			var drained = _patches.ToArray();
			_patches.Clear();

			return drained;
		}
	}

	/// <summary>
	/// Defers flushing until the returned scope is disposed, coalescing everything written inside it into one
	/// patch, one revision advance and one <see cref="Changed" />. Nests: only the outermost scope flushes.
	/// Without it, a step transition touching twenty states would emit twenty patches and twenty revisions,
	/// multiplied by the number of clients in a shared session.
	///
	/// <para>
	/// The batch belongs to the view, not to the thread that opened it, so two threads batching the same view
	/// coalesce into one patch and one thread's open batch defers the other's flush until it closes. The scope
	/// itself holds no lock and may be disposed on another thread than the one that opened it.
	/// </para>
	/// </summary>
	public IDisposable Batch()
	{
		using (_sync.Enter())
		{
			_batchDepth++;
		}

		return new UiBatchScope(this);
	}

	/// <summary>
	/// Delivers <paramref name="event" /> to the node it names and answers whether it was handled.
	///
	/// <para>
	/// <b>An unknown event is ignored, never fatal.</b> A node id this tree does not have, or an event name the
	/// node does not accept, returns <see cref="UiDispatchOutcome.Ignored" /> with a reason. That is the same
	/// posture the model takes towards an unknown message type: a newer renderer sending a newer event name to
	/// an older plugin has to leave a working session behind, and a throwing dispatch would turn it into a dead
	/// one.
	/// </para>
	///
	/// <para>
	/// <b>A wrong payload is rejected, never an exception.</b> <see cref="UiEvent.Data" /> is unvalidated data
	/// whose <see cref="System.Text.Json.JsonValueKind" /> is checked before it is decoded, so a client sending
	/// a string where a number belongs gets <see cref="UiDispatchOutcome.Rejected" /> with a reason. So does a
	/// write through a read-only binding, which mutates nothing.
	/// </para>
	///
	/// <para>
	/// <b>A handler may decline, which is not the same as faulting.</b> A handler returning
	/// <see cref="UiEventOutcome.Rejected" /> - a step whose fields are not filled in yet - rejects the dispatch
	/// with its own reason and does <b>not</b> raise <see cref="HandlerFaulted" />; a handler that throws rejects
	/// the dispatch and raises it. A host logs the second as an error and a renderer shows the first to the user,
	/// so the two cannot be the same signal. Either way, whatever the handler wrote before it stopped is still
	/// flushed: a refusal that carried a validation message has to deliver that message.
	/// </para>
	///
	/// <para>
	/// <b>A dispatch is one batch.</b> Everything the event changes - the bound value plus whatever its
	/// handlers write - lands in one patch and one revision advance, so an event touching five states is not
	/// five trees the client has to catch up with. An event that changed nothing advances nothing.
	/// </para>
	///
	/// <para>
	/// <b>Dispatch stays synchronous</b> even when a handler is not: the client needs its answer before the
	/// work can finish. An asynchronous handler's task is registered as pending work for
	/// <see cref="WhenIdleAsync" />; if it faults, <see cref="HandlerFaulted" /> reports it.
	/// </para>
	///
	/// <para>
	/// <b>A dispatch is atomic against concurrent writes.</b> The whole body runs under this view's own
	/// serialization, so a state written from a timer or a continuation lands either wholly before or wholly
	/// after it, never inside it. A synchronous handler therefore runs with that serialization held and must
	/// not block on work that has to write this view from another thread.
	/// </para>
	/// </summary>
	public UiDispatchResult Dispatch(UiEvent @event)
	{
		ArgumentNullException.ThrowIfNull(@event);

		using var scope = _sync.Enter();

		var target = FindShadow(_rootNode, @event.NodeId);

		if (target is null)
		{
			return UiDispatchResult.Ignored(
				$"No node with id '{@event.NodeId}' is in the tree at revision {_tree.Revision}.");
		}

		var element = target.Element;
		var isChange = string.Equals(@event.Name, _changeEvent, StringComparison.Ordinal);
		var input = element as IUiInputElement;
		var handlers = HandlersFor(element, @event.Name);

		// An input recognises change whether or not it advertises it: a read-only binding advertises nothing,
		// and a client writing through one has to be told it was refused rather than that nothing here answers
		// to the name.
		if (handlers.Count == 0 && !(isChange && input is not null))
		{
			return UiDispatchResult.Ignored($"The node '{target.Node.Id}' does not accept the event '{@event.Name}'.");
		}

		string? rejection = null;

		using (Batch())
		{
			// The binding is the write path unless the author supplied one: an input whose binding is read-only
			// but which registered its own change handler is writing through that handler, and refusing the event
			// would refuse the author's own arrangement.
			if (isChange && input is not null && (input.CanWriteValue || handlers.Count == 0))
			{
				rejection = WriteValue(input, target.Node.Id, @event);
			}

			rejection ??= InvokeHandlers(handlers, target.Node.Id, @event);
		}

		return rejection is null ? UiDispatchResult.Accepted() : UiDispatchResult.Rejected(rejection);
	}

	/// <summary>
	/// Waits until every asynchronous handler and every load started through this view has finished. The
	/// settling point a test needs: without it, asserting on a tree that is still loading is a race, and
	/// polling for it is the timing-brittle version of the same thing.
	/// </summary>
	/// <remarks>Work registered by work this call is already awaiting is awaited too, so a load that starts
	/// another one settles once rather than leaving the second one running.</remarks>
	public async Task WhenIdleAsync(CancellationToken cancellationToken = default)
	{
		while (true)
		{
			Task[] pending;

			// The gate is taken only to prune and snapshot: the await below holds nothing, so waiting for a load
			// cannot keep another thread out of the view the load will write to.
			using (_sync.Enter())
			{
				_pendingWork.RemoveAll(work => work.IsCompleted);
				pending = [.. _pendingWork];
			}

			if (pending.Length == 0)
			{
				return;
			}

			// Every registered task is either a fault-observing wrapper or a load that reports its fault as
			// Error, so nothing here is expected to throw - but a task that does must not turn settling into a
			// rethrow at a caller who only asked whether the work was done.
			try
			{
				await Task.WhenAll(pending).WaitAsync(cancellationToken).ConfigureAwait(false);
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
#pragma warning disable CA1031 // See the comment above: a settled fault belongs to whoever registered the work.
			catch (Exception)
#pragma warning restore CA1031
			{
				// Observed, and reported through HandlerFaulted by the wrapper that registered it.
			}
		}
	}

	/// <summary>Registers asynchronous work <see cref="WhenIdleAsync" /> has to wait for.</summary>
	internal void RegisterPendingWork(Task work)
	{
		// A thread holding another component's gate never takes this one - the registration waits for the
		// depth-0 drain instead. It is deferred rather than dropped because WhenIdleAsync on this view would
		// otherwise return while the load it is waiting for is still running.
		if (UiSyncRoot.IsForeign(_sync))
		{
			UiSyncRoot.DeferWork(this, work);

			return;
		}

		using (_sync.Enter())
		{
			if (work.IsCompleted || _pendingWork.Contains(work))
			{
				return;
			}

			_pendingWork.Add(work);
		}
	}

	/// <summary>Queues <paramref name="cell" /> for re-evaluation on the next flush, once however many times
	/// it is invalidated first.</summary>
	internal void MarkDirty(UiPropertyCell cell)
	{
		using (_sync.Enter())
		{
			if (cell.IsDirty)
			{
				return;
			}

			cell.IsDirty = true;
			_dirtyCells.Add(cell);
		}
	}

	/// <summary>Queues <paramref name="scope" /> for reconciliation on the next flush, once however many times
	/// it is invalidated first.</summary>
	internal void MarkStructural(UiStructuralScope scope)
	{
		using (_sync.Enter())
		{
			if (scope.IsQueued)
			{
				return;
			}

			scope.IsQueued = true;
			_dirtyScopes.Add(scope);
		}
	}

	/// <summary>Called by a state that changed after it marked its dependents.</summary>
	internal void OnStateChanged()
	{
		using (_sync.Enter())
		{
			// Nothing to flush into yet: the tree is still being materialized, so the marked cells are picked up
			// by the flush the constructor runs once it exists.
			if (!_built)
			{
				return;
			}

			if (_batchDepth == 0)
			{
				Flush();
			}
		}
	}

	/// <summary>This view's component gate, which every state it reads is serialized with.</summary>
	internal UiSyncRoot SyncRoot => _sync;

	/// <summary>Raises one queued notification: <see cref="HandlerFaulted" /> for a fault,
	/// <see cref="Changed" /> otherwise. Called outside the gate - see this type's remarks.</summary>
	internal void RaiseNotification(UiHandlerFaultEventArgs? fault)
	{
		if (fault is null)
		{
			Changed?.Invoke(this, EventArgs.Empty);

			return;
		}

		HandlerFaulted?.Invoke(this, fault);
	}

	/// <summary>Finds the shadow of the node with <paramref name="id" />, including inside fallback subtrees.
	/// Walked rather than indexed: a registry would have to be kept in step with every structural reconcile, and
	/// a dispatch is a user interaction, not a hot path.</summary>
	private static UiMaterializedNode? FindShadow(UiMaterializedNode node, string id)
	{
		if (string.Equals(node.Node.Id, id, StringComparison.Ordinal))
		{
			return node;
		}

		foreach (var child in node.Children)
		{
			if (FindShadow(child, id) is { } found)
			{
				return found;
			}
		}

		return node.Fallback is not null ? FindShadow(node.Fallback, id) : null;
	}

	/// <summary>Writes a change event's payload through the binding, returning the reason the dispatch is
	/// rejected or <c>null</c>. A <c>Bind.Custom</c> setter is plugin code like any handler, so a fault it throws
	/// is reported on <see cref="HandlerFaulted" /> rather than escaping the dispatch.</summary>
	private string? WriteValue(IUiInputElement input, string nodeId, UiEvent @event)
	{
		try
		{
			if (!input.TryWriteValue(@event.Data, out var rejection))
			{
				return rejection ?? $"The node '{nodeId}' refused the change event.";
			}
		}
#pragma warning disable CA1031 // A binding's setter is plugin code; any fault it produces is reported.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			RaiseHandlerFault(nodeId, @event.Name, exception);

			return $"The binding on '{nodeId}' faulted: {exception.Message}";
		}

		return null;
	}

	/// <summary>The handlers <paramref name="element" /> registered for <paramref name="name" />, in declaration
	/// order.</summary>
	private static List<UiEventHandler> HandlersFor(UiElement element, string name)
	{
		var handlers = new List<UiEventHandler>();

		foreach (var handler in element.Events)
		{
			if (string.Equals(handler.Name, name, StringComparison.Ordinal))
			{
				handlers.Add(handler);
			}
		}

		return handlers;
	}

	/// <summary>
	/// Runs <paramref name="handlers" />, returning the reason the dispatch is rejected or <c>null</c>.
	///
	/// <para>
	/// A handler that <b>declined</b> - it returned <see cref="UiEventOutcome.Rejected" /> - stops the remaining
	/// handlers and rejects the dispatch with its own reason, and no fault is raised: declining is an ordinary
	/// answer, not a defect. A handler that <b>faulted</b> stops the remaining handlers too, but is reported on
	/// <see cref="HandlerFaulted" /> as well, because that one is a defect. An asynchronous handler is registered
	/// as pending work; its fault is reported when it lands, and its outcome cannot be the dispatch's answer,
	/// which has already been given.
	/// </para>
	/// </summary>
	private string? InvokeHandlers(List<UiEventHandler> handlers, string nodeId, UiEvent @event)
	{
		var data = new UiEventData(@event.Data);

		foreach (var handler in handlers)
		{
			if (handler.Synchronous is { } synchronous)
			{
				UiEventOutcome outcome;

				try
				{
					outcome = synchronous(data);
				}
#pragma warning disable CA1031 // A handler is plugin code; any fault it produces is reported, never rethrown.
				catch (Exception exception)
#pragma warning restore CA1031
				{
					RaiseHandlerFault(nodeId, @event.Name, exception);

					return $"The '{@event.Name}' handler on '{nodeId}' faulted: {exception.Message}";
				}

				if (outcome.RejectionReason is { } rejectionReason)
				{
					return rejectionReason;
				}

				continue;
			}

			if (handler.Asynchronous is { } asynchronous)
			{
				RegisterPendingWork(ObserveAsync(asynchronous, data, nodeId, @event.Name));
			}
		}

		return null;
	}

	/// <summary>Starts an asynchronous handler and wraps it so its fault is reported rather than left on a task
	/// nobody awaits.</summary>
	private async Task ObserveAsync(
		Func<UiEventData, CancellationToken, Task<UiEventOutcome>> handler,
		UiEventData data,
		string nodeId,
		string eventName)
	{
		try
		{
			await handler(data, CancellationToken.None).ConfigureAwait(false);
		}
#pragma warning disable CA1031 // See InvokeHandlers: a handler is plugin code and its fault is reported.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			RaiseHandlerFault(nodeId, eventName, exception);
		}
	}

	private void RaiseHandlerFault(string nodeId, string eventName, Exception exception)
		=> UiSyncRoot.Raise(this, new UiHandlerFaultEventArgs(nodeId, eventName, exception));

	private void EndBatch()
	{
		using (_sync.Enter())
		{
			_batchDepth--;

			if (_batchDepth == 0)
			{
				Flush();
			}
		}
	}

	private void Flush()
	{
		// A provider that writes state while it is being evaluated would otherwise re-enter the flush and
		// interleave two patches. The write still marks its cells, so the next flush picks them up.
		if (_flushing)
		{
			return;
		}

		List<UiPatchOperation> operations = [];

		_flushing = true;

		try
		{
			// Structure first: a cell whose node was just removed must not contribute a property operation, and
			// the reconciler is what releases it.
			ReconcileStructure(operations);
			DiffProperties(operations);
			Rebuild(_rootNode);
		}
		finally
		{
			_flushing = false;
		}

		if (operations.Count == 0)
		{
			return;
		}

		var fromRevision = _tree.Revision;
		var toRevision = fromRevision + 1;

		_tree = _tree with { Revision = toRevision, Root = _rootNode.Node };
		_patches.Add(new UiPatch
		{
			FromRevision = fromRevision,
			ToRevision = toRevision,
			Operations = operations,
		});

		// Queued rather than raised: the patch is enqueued and the tree swapped under the gate first, so a
		// handler that drains can never see a Changed whose patch is not there yet, and it runs outside the gate
		// so it may write and dispatch straight back in.
		UiSyncRoot.Raise(this, null);
	}

	/// <summary>Reconciles every queued structural scope into its operations.</summary>
	private void ReconcileStructure(List<UiPatchOperation> operations)
	{
		// Indexed, not foreach: reconciling a scope can queue another one, and it can release one that is
		// already queued.
		for (var index = 0; index < _dirtyScopes.Count; index++)
		{
			var scope = _dirtyScopes[index];
			scope.IsQueued = false;

			if (scope.IsReleased)
			{
				continue;
			}

			_reconciler.Reconcile(scope, operations);
		}

		_dirtyScopes.Clear();
	}

	/// <summary>Re-evaluates the dirty cells, emits one <c>set-properties</c> per node whose properties
	/// actually changed, and flags the spine to those nodes for rebuilding.</summary>
	private void DiffProperties(List<UiPatchOperation> operations)
	{
		var changedNodes = new List<UiMaterializedNode>();

		// Indexed, not foreach: re-evaluating a cell can queue another one.
		for (var index = 0; index < _dirtyCells.Count; index++)
		{
			var cell = _dirtyCells[index];
			cell.IsDirty = false;

			// The node this cell fed left the tree in this same flush; a set-properties naming it would be
			// inapplicable.
			if (cell.IsReleased)
			{
				continue;
			}

			var change = cell.Reevaluate(out var value);
			if (change == UiCellChange.Unchanged || cell.Owner is not { } owner)
			{
				continue;
			}

			if (owner.PendingSets is null && owner.PendingRemovals is null)
			{
				changedNodes.Add(owner);
			}

			if (change == UiCellChange.Removed)
			{
				(owner.PendingRemovals ??= []).Add(cell.Key);
			}
			else
			{
				(owner.PendingSets ??= new Dictionary<string, JsonElement>(StringComparer.Ordinal))[cell.Key] = value;
			}
		}

		_dirtyCells.Clear();

		foreach (var node in changedNodes)
		{
			operations.Add(new UiPatchOperation
			{
				Op = UiPatchOperations.SetProperties,
				NodeId = node.Node.Id,
				Properties = node.PendingSets,
				RemovedProperties = node.PendingRemovals,
			});

			node.MarkForRebuild();
		}
	}

	/// <summary>Rebuilds the flagged spine bottom-up, applying each node's pending property changes. An
	/// unflagged subtree is not visited at all and keeps its node instance.</summary>
	private static void Rebuild(UiMaterializedNode node)
	{
		if (!node.NeedsRebuild)
		{
			return;
		}

		node.NeedsRebuild = false;

		foreach (var child in node.Children)
		{
			Rebuild(child);
		}

		if (node.Fallback is not null)
		{
			Rebuild(node.Fallback);
		}

		var properties = node.Node.Properties;

		if (node.PendingSets is not null || node.PendingRemovals is not null)
		{
			var updated = new Dictionary<string, JsonElement>(properties, StringComparer.Ordinal);

			if (node.PendingSets is not null)
			{
				foreach (var (key, value) in node.PendingSets)
				{
					updated[key] = value;
				}
			}

			if (node.PendingRemovals is not null)
			{
				foreach (var key in node.PendingRemovals)
				{
					updated.Remove(key);
				}
			}

			properties = updated;
			node.PendingSets = null;
			node.PendingRemovals = null;
		}

		var children = new List<UiNode>(node.Children.Count);
		foreach (var child in node.Children)
		{
			children.Add(child.Node);
		}

		node.Node = node.Node with
		{
			Properties = properties,
			Children = children,
			Fallback = node.Fallback?.Node,
		};
	}

	/// <summary>The handle <see cref="Batch" /> returns. Idempotent: a second disposal does nothing rather
	/// than closing a batch it does not own.</summary>
	private sealed class UiBatchScope : IDisposable
	{
		private UiView? _view;

		internal UiBatchScope(UiView view) => _view = view;

		public void Dispose()
		{
			var view = _view;

			if (view is null)
			{
				return;
			}

			_view = null;
			view.EndBatch();
		}
	}

	/// <summary>The event name a writable binding answers to. A local literal rather than a shared constant,
	/// which the configuration event vocabulary owns.</summary>
	private const string _changeEvent = "change";
}
