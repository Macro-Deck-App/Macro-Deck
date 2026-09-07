using System.Text.Json;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Caching;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Widgets;
using MacroDeckHost.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Widgets.ActionButton;

/// <summary>
/// Drives one Action Button widget's UI session: dispatches a press through
/// <see cref="IWidgetTriggerService" /> exactly like the REST/WebSocket handler does, mirrors the same
/// three pushes a legacy client is sent - the active state, the resolved label text and a stored-data
/// change - into its own tree via <see cref="IWidgetRenderSignals" />, and reports a refused or failed
/// press through <see cref="ActionExecutionStatusEvent" /> so an unclaimed failure still reaches the user.
///
/// <para>
/// <b>Two-phase construction</b>, for the reason <c>SliderWidgetSession</c> documents: <see cref="BuildEvents" />
/// is called first to get the handlers <see cref="ActionButtonWidgetView.Build" /> attaches to the tree,
/// then <see cref="Attach" /> once the resulting <see cref="UiView" /> exists.
/// </para>
/// </summary>
internal sealed class ActionButtonWidgetSession : IUiSession, IOriginAwareUiSession
{
	private readonly UiState<ActionButtonWidgetData> _configState;
	private readonly UiState<string?> _activeState;
	private readonly UiState<string?> _labelText;
	private readonly UiState<IReadOnlyDictionary<WidgetIconReference, UiResource>> _iconResourcesState;
	private readonly UiState<WidgetIconResolution> _iconProviderState;
	private readonly WidgetEntity _widget;
	private readonly IFolderCache _folderCache;
	private readonly IWidgetTriggerService _triggerService;
	private readonly IHostLockState _lockState;
	private readonly IWidgetIconResources _iconResources;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly WidgetStateSubscriptionTracker _stateSubscriptions;
	private readonly LabelSubscriptionTracker _labelSubscriptions;
	private readonly IWidgetRenderSignals _renderSignals;
	private readonly IUiTransport _uiTransport;
	private readonly bool _interactive;
	private readonly string? _variableScopeWidgetId;

	// A synthetic subscriber identity, exactly like a connected client's connection id would be, so this
	// session can register itself in the same trackers a legacy WebSocket subscription uses: that is what
	// makes WidgetStatePublisher.PublishIfChanged's HasSubscribers gate pass and
	// WidgetStateEvalBackgroundService treat this widget as live, with no second poller.
	private readonly string _connectionId = $"ui-session:{Guid.NewGuid():N}";

	private IDisposable? _stateSignalSubscription;
	private IDisposable? _labelSignalSubscription;
	private IDisposable? _dataSignalSubscription;
	private IDisposable? _iconInvalidatedSubscription;
	private IDisposable? _widgetIconChangedSubscription;
	private IDisposable? _variableChangedSubscription;

	// The label-subscription state key currently registered in _labelSubscriptions, or null when the
	// active state's label is not a Liquid template and so nothing is registered at all.
	private string? _subscribedLabelState;

	// Whether _activeState currently holds a state this session (or a signal it has observed) actually
	// chose, as opposed to ActionButtonWidgetData.InitialStateId's own first-state fallback - see
	// NextStateId. Starts true only when the stored data already names an explicit active state.
	private bool _activeStateIsExplicit;

	// A dispatch and a signal raised from a background service's own thread both reach this session. The
	// view is safe against that on its own; what needs the lock is this session's own bookkeeping -
	// _disposed, the active state and the label subscription it is keyed to - which a signal handler and a
	// press both rewrite. See SliderWidgetSession's _viewSync for the identical reasoning.
	private readonly Lock _viewSync = new();

	private UiView? _view;

	// Guarded by _viewSync - see DisposeAsync's own comment for why the barrier has to live there.
	private bool _disposed;

	// Guards the icon-resolution coalescing loop below - the same shape RequestPushAsync/PushLoopAsync use
	// in SliderWidgetSession, but with no value to carry across requests: a request only ever means "resolve
	// whatever the current config's icon ids are", so the loop simply re-reads _configState each pass rather
	// than threading a payload through _iconSync.
	private readonly Lock _iconSync = new();
	private bool _iconResolveRunning;
	private bool _iconResolveDirty;
	private Task? _iconResolveTask;

	// The same coalescing shape, for a scoped Preview's draft label - see ScheduleDraftLabelResolution.
	private readonly Lock _labelSync = new();
	private bool _labelResolveRunning;
	private bool _labelResolveDirty;
	private Task? _labelResolveTask;

	// Cancelled on Dispose so an in-flight resolve stops touching _view once it is gone - see DisposeAsync.
	private readonly CancellationTokenSource _lifetime = new();

	private static readonly Dictionary<string, string> _triggerTypeByEventName
		= new(StringComparer.Ordinal)
		{
			[UiComponentEvents.Press] = WidgetTriggerTypes.ShortPress,
			[UiComponentEvents.LongPress] = WidgetTriggerTypes.LongPress,
			[UiComponentEvents.PressStart] = WidgetTriggerTypes.TouchStart,
			[UiComponentEvents.PressEnd] = WidgetTriggerTypes.TouchEnd,
		};

	/// <param name="interactive">Whether this session accepts presses at all - true only for the Widget
	/// surface. The Preview surface builds with this <c>false</c>, which is what makes
	/// <see cref="BuildEvents" /> return no handlers, a press at it a no-op before any of this session's
	/// own code runs, and this session never register into the live trackers below - a preview draft has
	/// no persisted widget for another client to ever legitimately subscribe to.</param>
	/// <param name="variableScopeWidgetId">The stored widget a Preview draft belongs to, so its label's
	/// widget-scoped variables resolve against the same values the deck tile sees - see
	/// <c>UiWidgetSurfaceAttributes.VariableScopeWidgetId</c>. Null for a Widget surface, which resolves
	/// against itself, and for a draft with nothing to resolve against (a sample, or a widget that has
	/// never been saved), which keeps resolving global variables alone.</param>
	public ActionButtonWidgetSession(
		UiState<ActionButtonWidgetData> configState,
		UiState<string?> activeState,
		UiState<string?> labelText,
		UiState<IReadOnlyDictionary<WidgetIconReference, UiResource>> iconResourcesState,
		UiState<WidgetIconResolution> iconProviderState,
		WidgetEntity widget,
		IFolderCache folderCache,
		IWidgetTriggerService triggerService,
		IHostLockState lockState,
		IWidgetIconResources iconResources,
		IServiceScopeFactory scopeFactory,
		WidgetStateSubscriptionTracker stateSubscriptions,
		LabelSubscriptionTracker labelSubscriptions,
		IWidgetRenderSignals renderSignals,
		IUiTransport uiTransport,
		bool interactive,
		string? variableScopeWidgetId = null)
	{
		ArgumentNullException.ThrowIfNull(configState);
		ArgumentNullException.ThrowIfNull(activeState);
		ArgumentNullException.ThrowIfNull(labelText);
		ArgumentNullException.ThrowIfNull(iconResourcesState);
		ArgumentNullException.ThrowIfNull(iconProviderState);
		ArgumentNullException.ThrowIfNull(widget);
		ArgumentNullException.ThrowIfNull(folderCache);
		ArgumentNullException.ThrowIfNull(triggerService);
		ArgumentNullException.ThrowIfNull(lockState);
		ArgumentNullException.ThrowIfNull(iconResources);
		ArgumentNullException.ThrowIfNull(scopeFactory);
		ArgumentNullException.ThrowIfNull(stateSubscriptions);
		ArgumentNullException.ThrowIfNull(labelSubscriptions);
		ArgumentNullException.ThrowIfNull(renderSignals);
		ArgumentNullException.ThrowIfNull(uiTransport);

		_configState = configState;
		_activeState = activeState;
		_labelText = labelText;
		_iconResourcesState = iconResourcesState;
		_iconProviderState = iconProviderState;
		_widget = widget;
		_folderCache = folderCache;
		_triggerService = triggerService;
		_lockState = lockState;
		_iconResources = iconResources;
		_scopeFactory = scopeFactory;
		_stateSubscriptions = stateSubscriptions;
		_labelSubscriptions = labelSubscriptions;
		_renderSignals = renderSignals;
		_uiTransport = uiTransport;
		_interactive = interactive;
		_variableScopeWidgetId = variableScopeWidgetId;
		_activeStateIsExplicit = configState.Peek().StoredActiveStateId is not null;
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	/// <summary>A paired synchronous/asynchronous handler per declared trigger name - empty for a
	/// non-interactive (Preview) session, which is what makes a Preview inert against a dispatched event:
	/// with no handler registered under any name, <see cref="UiView.Dispatch" /> ignores it before any of
	/// this session's code runs.</summary>
	internal IReadOnlyList<UiEventHandler> BuildEvents()
	{
		if (!_interactive)
		{
			return [];
		}

		var handlers = new List<UiEventHandler>();

		foreach (var name in _configState.Peek().DeclaredTriggers())
		{
			var eventName = name;
			// The Func<UiEventData, UiEventOutcome> overload, deliberately: On(string, Action) also accepts a
			// zero-arg lambda whose body is a method call by silently discarding its result, which would drop
			// HandleInteraction's rejection - a locked-host refusal has to actually reach UiView.Dispatch.
			handlers.Add(UiEventHandler.On(eventName, (UiEventData _) => HandleInteraction(eventName)));
			handlers.Add(UiEventHandler.OnAsync(eventName, (data, ct) => HandlePressAsync(eventName, ct)));
		}

		return handlers;
	}

	internal void Attach(UiView view)
	{
		ArgumentNullException.ThrowIfNull(view);

		_view = view;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		if (!_interactive)
		{
			// A draft has no stored label for LabelRenderBackgroundService to re-resolve and push, and the
			// widget-variable index that gates that path only knows the stored one - so a scoped preview
			// keeps up with a changing variable by re-rendering its own draft label instead.
			if (_variableScopeWidgetId is not null)
			{
				_variableChangedSubscription = _renderSignals.SubscribeVariableChanged(ScheduleDraftLabelResolution);
			}

			return;
		}

		var widgetKey = _widget.Id.ToString();
		_stateSubscriptions.Add(_connectionId, widgetKey);

		var initialStateId = _activeState.Peek();
		var initialLabel = _configState.Peek().Resolve(initialStateId).Label;

		if (VariableTemplateRenderer.ContainsLiquid(initialLabel))
		{
			_subscribedLabelState = LabelGroups.Normalize(initialStateId ?? "off");
			_labelSubscriptions.Add(_connectionId, widgetKey, _subscribedLabelState);
		}

		_stateSignalSubscription = _renderSignals.SubscribeState(widgetKey, OnStateSignal);
		_labelSignalSubscription = _renderSignals.SubscribeLabel(widgetKey, OnLabelSignal);
		_dataSignalSubscription = _renderSignals.SubscribeDataChanged(widgetKey, OnDataChanged);
		_iconInvalidatedSubscription = _renderSignals.SubscribeIconInvalidated(OnIconInvalidated);
		_widgetIconChangedSubscription = _renderSignals.SubscribeWidgetIconChanged(widgetKey, ScheduleIconResolution);
	}

	public UiTree BuildTree()
	{
		lock (_viewSync)
		{
			return _view!.Tree;
		}
	}

	/// <summary>Awaits whatever icon-resolution pass is currently in flight, or returns immediately when
	/// none is. Internal rather than private so a test can settle deterministically on the work
	/// <see cref="OnDataChanged" /> starts fire-and-forget - the same reason <c>SliderWidgetSession</c>
	/// exposes <c>PollOnceAsync</c>.</summary>
	internal Task WaitForIconResolutionAsync()
	{
		Task? task;

		lock (_iconSync)
		{
			task = _iconResolveTask;
		}

		return task ?? Task.CompletedTask;
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		lock (_viewSync)
		{
			return _view!.DrainPatches();
		}
	}

	public void Dispatch(UiEvent uiEvent) => Dispatch(uiEvent, null);

	public void Dispatch(UiEvent uiEvent, string? originClientId)
	{
		lock (_viewSync)
		{
			_pendingOriginClientId = originClientId;
			_view!.Dispatch(uiEvent);
		}
	}

	// Captured synchronously inside Dispatch, above, before the paired sync then async handlers for the
	// same event run - IUiSession.Dispatch is never called concurrently with itself, so the async handler
	// always reads the origin that belongs to its own dispatch.
	private string? _pendingOriginClientId;

	public async ValueTask DisposeAsync()
	{
		// _disposed is set - and _subscribedLabelState snapshotted - under _viewSync first, before
		// anything is unsubscribed or removed: WidgetRenderSignals.Raise invokes its handlers outside its
		// own lock, so a signal already in flight can still call OnStateSignal/OnLabelSignal after
		// Dispose() below returns, racing this cleanup. Every one of those handlers takes _viewSync before
		// touching _labelSubscriptions (via RekeyLabelSubscription), so setting the flag under the same
		// lock is what guarantees no handler can register a new entry once cleanup has read what to
		// remove - without it, a late handler's RekeyLabelSubscription can re-Add after this method's own
		// Remove already ran, leaving an orphan entry that keeps HasSubscribers true for good (finding 5).
		string? subscribedLabelState;

		lock (_viewSync)
		{
			_disposed = true;
			subscribedLabelState = _subscribedLabelState;
		}

		_stateSignalSubscription?.Dispose();
		_labelSignalSubscription?.Dispose();
		_dataSignalSubscription?.Dispose();
		_iconInvalidatedSubscription?.Dispose();
		_widgetIconChangedSubscription?.Dispose();
		_variableChangedSubscription?.Dispose();

		if (_interactive)
		{
			var widgetKey = _widget.Id.ToString();
			_stateSubscriptions.Remove(_connectionId, widgetKey);

			if (subscribedLabelState is { } state)
			{
				_labelSubscriptions.Remove(_connectionId, widgetKey, state);
			}
		}

		await _lifetime.CancelAsync().ConfigureAwait(false);

		Task? iconResolveTask;

		lock (_iconSync)
		{
			iconResolveTask = _iconResolveTask;
		}

		Task? labelResolveTask;

		lock (_labelSync)
		{
			labelResolveTask = _labelResolveTask;
		}

		await AwaitCancelled(iconResolveTask).ConfigureAwait(false);
		await AwaitCancelled(labelResolveTask).ConfigureAwait(false);

		_lifetime.Dispose();
	}

	private static async Task AwaitCancelled(Task? task)
	{
		if (task is null)
		{
			return;
		}

		try
		{
			await task.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
	}

	/// <summary>
	/// The synchronous half of a press dispatch: refuses while the host is locked <b>before</b> touching
	/// any state - reporting the refusal through <see cref="ActionExecutionStatusEvent" /> so an unclaimed
	/// failure still reaches the user exactly as a locked host explained itself to the retired RPC client -
	/// then, for a <c>press</c> on a button that can advance its own state with no provider or mapping
	/// authoritative for it, advances this session's own view of the active state so the tile repaints
	/// immediately. The actual persisted advance happens inside
	/// <see cref="IWidgetTriggerService.ExecuteAsync" />, driven by the same deterministic wrap-around
	/// rule, so the two agree as long as nothing external changed the widget's state between this call and
	/// the persisted write - and if something did, the live state signal corrects it right after.
	/// </summary>
	private UiEventOutcome HandleInteraction(string eventName)
	{
		if (_lockState.IsLocked)
		{
			EmitRefusal(eventName);

			return UiEventOutcome.Rejected("The host is locked.");
		}

		if (eventName == UiComponentEvents.Press && _configState.Peek().CanAdvanceState)
		{
			var next = NextStateId();

			if (next is not null)
			{
				ApplyActiveState(next);
			}
		}

		return UiEventOutcome.Accepted;
	}

	/// <summary>
	/// Mirrors <see cref="ActionButtonStateJson.NextStateId" /> exactly, including for a freshly placed
	/// button that has never had an explicit active state: <see cref="_activeState" /> already carries
	/// <c>ActionButtonWidgetData.InitialStateId</c>'s first-state fallback (the tree needs something to
	/// render), so using it as "current" unconditionally would advance from state[0] instead of from "no
	/// current" the way the persisted rule does - landing this session on state[1] while the host
	/// persists state[0], with nothing to ever correct it if <c>PublishIfChanged</c> sees no change to
	/// signal (finding 4).
	/// </summary>
	private string? NextStateId()
	{
		var ids = _configState.Peek().States.Select(s => s.Id).ToList();
		var current = _activeStateIsExplicit ? _activeState.Peek() : _configState.Peek().StoredActiveStateId;

		return ActionButtonStateJson.NextStateId(ids, current);
	}

	private Task HandlePressAsync(string eventName, CancellationToken cancellationToken)
	{
		var triggerType = _triggerTypeByEventName[eventName];
		var originClientId = _pendingOriginClientId;

		return RunAndReportAsync(triggerType, originClientId, cancellationToken);
	}

	private async Task RunAndReportAsync(string triggerType,
		string? originClientId,
		CancellationToken cancellationToken)
	{
		// The profile cache replaces the widget instance on every edit, so the entity this session opened
		// with is a pre-edit snapshot whose flows a press must not run (issue #683).
		var widget = _folderCache.GetAllFolders()
			.SelectMany(folder => folder.Widgets)
			.FirstOrDefault(candidate => candidate.Id == _widget.Id);

		if (widget is null)
		{
			return;
		}

		// No origin device: a press dispatched through a UI session comes from a deck client, which is
		// identified by its client id. A device origin belongs to a plugin-provided device (ADR 0068).
		var dispatch = await _triggerService
			.ExecuteAsync(widget, triggerType, originClientId, originDeviceId: (Guid?)null, cancellationToken)
			.ConfigureAwait(false);

		// dispatch.Result is null when the flow outran WidgetTriggerService's own bound and was detached -
		// ActionExecutionCoordinator.PublishWhenDone already reports that one once it finishes, so reporting
		// it again here would double the toast an unclaimed failure produces.
		if (dispatch.Result is not { } result || string.IsNullOrEmpty(originClientId))
		{
			return;
		}

		var evt = ActionExecutionDtoMapper.ToStatusEvent(result, _widget.Id.ToString(), triggerType);

		if (!ActionExecutionDtoMapper.IsSuccess(evt.Status))
		{
			await _uiTransport.SendToGroup(UiClientGroups.For(originClientId), evt, cancellationToken)
				.ConfigureAwait(false);
		}
	}

	private void EmitRefusal(string eventName)
	{
		var originClientId = _pendingOriginClientId;

		if (string.IsNullOrEmpty(originClientId))
		{
			return;
		}

		var evt = new ActionExecutionStatusEvent
		{
			ExecutionId = Guid.NewGuid().ToString(),
			Status = ActionExecutionStatus.Failed,
			WidgetId = _widget.Id.ToString(),
			TriggerType = _triggerTypeByEventName.GetValueOrDefault(eventName),
			Error = new TransportError
				{ Code = ActionExecutionErrorCodes.HostLocked, Message = AppStrings.Errors.Common.HostLocked() }
		};

		_ = _uiTransport.SendToGroup(UiClientGroups.For(originClientId), evt);
	}

	/// <summary>Writes <paramref name="stateId" /> as the active state and, for a label that is not a
	/// Liquid template, its raw text too - a template's resolved text arrives only through
	/// <see cref="OnLabelSignal" />, never computed here, so the tree never carries anything but a fully
	/// resolved string. Also re-keys the label-subscription tracker registration to the new state. Callers
	/// are already holding <see cref="_viewSync" /> - synchronously from <see cref="Dispatch" /> for a
	/// press, or explicitly in <see cref="OnStateSignal" /> for an external one.</summary>
	private void ApplyActiveState(string? stateId)
	{
		var rawLabel = _configState.Peek().Resolve(stateId).Label;
		var isLiquid = VariableTemplateRenderer.ContainsLiquid(rawLabel);

		using (_view!.Batch())
		{
			_activeState.Value = stateId;

			if (!isLiquid)
			{
				_labelText.Value = rawLabel;
			}
		}

		_activeStateIsExplicit = true;
		RekeyLabelSubscription(stateId, isLiquid);
	}

	/// <summary>Callers already hold <see cref="_viewSync" /> (see the call sites), which is what makes the
	/// <see cref="_disposed" /> check below race-free against <see cref="DisposeAsync" />: once disposal has
	/// set the flag under the same lock, no call arriving here afterward - however late a signal handler
	/// runs - can register a new entry in <see cref="_labelSubscriptions" /> for a session already gone.</summary>
	private void RekeyLabelSubscription(string? stateId, bool isLiquid)
	{
		if (_disposed)
		{
			return;
		}

		var widgetKey = _widget.Id.ToString();
		var newState = isLiquid ? LabelGroups.Normalize(stateId ?? "off") : null;

		if (string.Equals(newState, _subscribedLabelState, StringComparison.Ordinal))
		{
			return;
		}

		if (_subscribedLabelState is { } previous)
		{
			_labelSubscriptions.Remove(_connectionId, widgetKey, previous);
		}

		if (newState is not null)
		{
			_labelSubscriptions.Add(_connectionId, widgetKey, newState);
		}

		_subscribedLabelState = newState;
	}

	/// <summary>The active-state authority - a provider poll, a state mapping, or the explicit "Set"/"Toggle
	/// Button State" action - reaching this session exactly as it reaches a legacy client subscribed to the
	/// same group, via <see cref="WidgetStatePublisher.PublishIfChanged" />'s signal.</summary>
	private void OnStateSignal(WidgetStateUpdatedEvent evt)
	{
		lock (_viewSync)
		{
			ApplyActiveState(evt.StateId);
		}
	}

	/// <summary>The resolved label text for whichever state this session is currently subscribed to,
	/// reaching this session exactly as it reaches a legacy client subscribed to the same group. A push for
	/// a state this session is not currently showing is ignored - the label tracker registration only ever
	/// names the active one.</summary>
	private void OnLabelSignal(LabelTextUpdatedEvent evt)
	{
		var pushedState = LabelGroups.Normalize(evt.State);

		if (!string.Equals(pushedState, _subscribedLabelState, StringComparison.Ordinal))
		{
			return;
		}

		lock (_viewSync)
		{
			using (_view!.Batch())
			{
				_labelText.Value = evt.Text;
			}
		}
	}

	/// <summary>
	/// A stored-data change - including a plugin's <c>WidgetAppearanceService</c> recolouring the button
	/// mid-session - reaching this session exactly as the resulting <c>WidgetUpdatedEvent</c> reaches a
	/// legacy client. Re-parses the fresh data and patches every appearance property that changed, <b>except</b>
	/// when the edit changed the declared trigger set: that is structural, because handler registration was
	/// baked into the tree at build time, so this session faults instead and lets the client reopen with a
	/// fresh tree.
	/// </summary>
	private void OnDataChanged(WidgetEntity widget)
	{
		var newConfig = ActionButtonWidgetData.Parse(ParseData(widget.Data));

		if (!newConfig.DeclaredTriggers().SequenceEqual(_configState.Peek().DeclaredTriggers(), StringComparer.Ordinal))
		{
			Faulted?.Invoke(this,
				new UiSessionFaultedEventArgs("The button's declared events changed and can no longer be applied live.",
					null));

			return;
		}

		var stateId = _activeState.Peek();
		var rawLabel = newConfig.Resolve(stateId).Label;
		var isLiquid = VariableTemplateRenderer.ContainsLiquid(rawLabel);

		lock (_viewSync)
		{
			using (_view!.Batch())
			{
				_configState.Value = newConfig;

				// The label lives in its own state cell, not in the config, so re-parsing alone leaves it
				// at whatever the previous data resolved to. A button is created with an empty label and
				// gets one on the editor's first save, so without this the tile a session opened at
				// creation keeps rendering no label at all until that session is rebuilt. A template's
				// text still arrives only through OnLabelSignal - see ApplyActiveState.
				if (!isLiquid)
				{
					_labelText.Value = rawLabel;
				}
			}
		}

		RekeyLabelSubscription(stateId, isLiquid);
		ScheduleIconResolution();
	}

	/// <summary>
	/// An icon this session has already resolved changed bytes or was deleted - <see cref="IWidgetRenderSignals.RaiseIconInvalidated" />
	/// reaching this session exactly as the eviction reached <c>IWidgetIconResources</c>' own cache. Every
	/// open session sees every invalidation (icon ids have no owning widget to key the signal on), so this
	/// filters to whether the current config actually references <paramref name="iconId" /> before doing
	/// anything. The signal only ever names an icon-pack id, so the filter compares each reference's
	/// <see cref="WidgetIconReference.Reference" /> as a GUID rather than the raw dictionary key - the
	/// resolved-resource dictionary is keyed by the whole typed reference, not by a bare id. Dropping the
	/// resolved entry is what makes it "missing" again to <see cref="ResolveMissingIconsAsync" /> - without
	/// this, a re-imported icon keeps drawing its old bytes for the rest of this session's lifetime, since
	/// that loop only ever resolves a reference it has never seen before, never one it already resolved
	/// once (finding 7).
	/// </summary>
	private void OnIconInvalidated(Guid iconId)
	{
		bool changed;

		lock (_viewSync)
		{
			// Same barrier as RekeyLabelSubscription - see its own comment - so a signal already in
			// flight when DisposeAsync ran can never touch _iconResourcesState, or schedule a resolution
			// loop, for a session that is already gone.
			if (_disposed)
			{
				return;
			}

			var stillReferenced = _configState.Peek().AllIconReferences()
				.Any(reference => IsIconPackMatch(reference, iconId));

			var current = _iconResourcesState.Peek();
			var stillResolved = current.Keys.Any(reference => IsIconPackMatch(reference, iconId));

			changed = stillReferenced && stillResolved;

			if (changed)
			{
				var withoutStale = current.Where(pair => !IsIconPackMatch(pair.Key, iconId))
					.ToDictionary(pair => pair.Key, pair => pair.Value);

				using (_view!.Batch())
				{
					_iconResourcesState.Value = withoutStale;
				}
			}
		}

		if (changed)
		{
			ScheduleIconResolution();
		}
	}

	private static bool IsIconPackMatch(WidgetIconReference reference, Guid iconId)
		=> reference.Type == WidgetIconReference.IconPackType &&
			Guid.TryParse(reference.Reference, out var parsed) &&
			parsed == iconId;

	/// <summary>
	/// Kicks off resolving whatever icon ids <see cref="_configState" /> currently references but this
	/// session's <see cref="_iconResourcesState" /> has not resolved yet - the gap that otherwise leaves a
	/// button repainting its colours and label on a data change but not an icon it has never seen before.
	/// Coalesced the same way <c>SliderWidgetSession.RequestPushAsync</c> coalesces its pushes: a request
	/// arriving while one is already in flight does not start a second one, it just marks the running loop
	/// dirty so it takes one more trailing pass once the current one finishes, picking up whatever config is
	/// newest by then.
	/// </summary>
	private void ScheduleIconResolution()
	{
		lock (_iconSync)
		{
			_iconResolveDirty = true;

			if (_iconResolveRunning)
			{
				return;
			}

			_iconResolveRunning = true;
			_iconResolveTask = Task.Run(() => RunIconResolutionLoopAsync(_lifetime.Token), CancellationToken.None);
		}
	}

	private async Task RunIconResolutionLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				lock (_iconSync)
				{
					_iconResolveDirty = false;
				}

				await ResolveMissingIconsAsync(cancellationToken).ConfigureAwait(false);
				await ResolveIconProviderAsync(cancellationToken).ConfigureAwait(false);

				lock (_iconSync)
				{
					if (!_iconResolveDirty)
					{
						return;
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
			// The session was disposed; the loop simply stops - DisposeAsync awaits this task before
			// returning, so nothing keeps running against a session nobody holds a reference to any more.
		}
		finally
		{
			lock (_iconSync)
			{
				_iconResolveRunning = false;
			}
		}
	}

	/// <summary>Resolves every icon id the current config references that this session has not already
	/// resolved, entirely outside <see cref="_viewSync" /> - resolution is async and must never block a
	/// signal callback or run inside the lock that guards <see cref="_view" /> - and merges whatever
	/// resolved into <see cref="_iconResourcesState" /> in one batch. An icon that fails to resolve is simply
	/// left out: absence is what <see cref="ActionButtonWidgetView" /> already treats as "draw no source",
	/// never a stale or broken handle.</summary>
	private async Task ResolveMissingIconsAsync(CancellationToken cancellationToken)
	{
		var known = _iconResourcesState.Peek();
		var missing = _configState.Peek().AllIconReferences().Where(reference => !known.ContainsKey(reference))
			.ToArray();

		if (missing.Length == 0)
		{
			return;
		}

		var resolved = new Dictionary<WidgetIconReference, UiResource>();

		foreach (var reference in missing)
		{
			cancellationToken.ThrowIfCancellationRequested();

			var resource = await _iconResources.ResolveAsync(reference, cancellationToken).ConfigureAwait(false);

			if (resource is not null)
			{
				resolved[reference] = resource;
			}
		}

		if (resolved.Count == 0)
		{
			return;
		}

		cancellationToken.ThrowIfCancellationRequested();

		lock (_viewSync)
		{
			var merged = new Dictionary<WidgetIconReference, UiResource>(_iconResourcesState.Peek());

			foreach (var (reference, resource) in resolved)
			{
				merged[reference] = resource;
			}

			using (_view!.Batch())
			{
				_iconResourcesState.Value = merged;
			}
		}
	}

	/// <summary>
	/// Re-resolves whatever this button's icon-provider action currently contributes, exactly as
	/// <see cref="ResolveMissingIconsAsync" /> re-resolves configured icon references: entirely outside
	/// <see cref="_viewSync" /> since the read is async and may call out to a plugin, merging the outcome
	/// in one batch. <see cref="IWidgetIconService" /> is scoped, so a scope is opened per call - this
	/// session outlives any single one of them.
	/// </summary>
	private async Task ResolveIconProviderAsync(CancellationToken cancellationToken)
	{
		await using var scope = _scopeFactory.CreateAsyncScope();
		var iconService = scope.ServiceProvider.GetRequiredService<IWidgetIconService>();
		var resolution = await iconService.Resolve(_widget.Id, cancellationToken).ConfigureAwait(false);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_viewSync)
		{
			if (_disposed || resolution == _iconProviderState.Peek())
			{
				return;
			}

			using (_view!.Batch())
			{
				_iconProviderState.Value = resolution;
			}
		}
	}

	/// <summary>
	/// Kicks off re-rendering this scoped Preview's draft label against its scope widget's variables, the
	/// same way <see cref="ScheduleIconResolution" /> re-resolves icons: a request arriving while one is
	/// already in flight only marks the running loop dirty, so a burst of variable writes settles into one
	/// trailing pass rather than a render each.
	/// </summary>
	private void ScheduleDraftLabelResolution()
	{
		lock (_labelSync)
		{
			_labelResolveDirty = true;

			if (_labelResolveRunning)
			{
				return;
			}

			_labelResolveRunning = true;
			_labelResolveTask = Task.Run(() => RunDraftLabelResolutionLoopAsync(_lifetime.Token),
				CancellationToken.None);
		}
	}

	private async Task RunDraftLabelResolutionLoopAsync(CancellationToken cancellationToken)
	{
		try
		{
			while (true)
			{
				lock (_labelSync)
				{
					_labelResolveDirty = false;
				}

				await ResolveDraftLabelAsync(cancellationToken).ConfigureAwait(false);

				lock (_labelSync)
				{
					if (!_labelResolveDirty)
					{
						return;
					}
				}
			}
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			lock (_labelSync)
			{
				_labelResolveRunning = false;
			}
		}
	}

	/// <summary>Renders the draft's own raw label against <see cref="_variableScopeWidgetId" />'s variable
	/// context - exactly what the provider did to open this session - entirely outside
	/// <see cref="_viewSync" />, since the render is async and may read the variable store.
	/// <see cref="ILabelTextService" /> is scoped and this session outlives any single scope.</summary>
	private async Task ResolveDraftLabelAsync(CancellationToken cancellationToken)
	{
		var rawLabel = _configState.Peek().Resolve(_activeState.Peek()).Label;

		await using var scope = _scopeFactory.CreateAsyncScope();
		var labelTextService = scope.ServiceProvider.GetRequiredService<ILabelTextService>();
		var resolved = await labelTextService
			.ResolvePreview(new LabelImagePreviewRequest { Label = rawLabel, ScopeRefId = _variableScopeWidgetId },
				cancellationToken)
			.ConfigureAwait(false);

		cancellationToken.ThrowIfCancellationRequested();

		lock (_viewSync)
		{
			if (_disposed || string.Equals(resolved, _labelText.Peek(), StringComparison.Ordinal))
			{
				return;
			}

			using (_view!.Batch())
			{
				_labelText.Value = resolved;
			}
		}
	}

	private static JsonElement ParseData(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
		{
			return default;
		}

		using var document = JsonDocument.Parse(json);

		return document.RootElement.Clone();
	}
}
