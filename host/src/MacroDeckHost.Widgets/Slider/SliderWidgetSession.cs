using System.Globalization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Components;
using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Widgets;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Widgets.Slider;

/// <summary>The session's one piece of live state: the range, the value and the attributes the track and
/// its readout are drawn against, all read from the bound variable - its own volatile range where it
/// declares one, and the widget config's Min/Max/Step per field where it does not. Everything the view
/// shows - the level, the step, the value text - is a pure function of this, which is what makes a
/// variable change or an interaction a <c>set-properties</c> patch rather than a structural
/// reconcile.</summary>
internal sealed record SliderWidgetReadout(
	bool Found,
	double Min,
	double Max,
	double Step,
	double Value,
	string? SemanticKind = null,
	string? Unit = null,
	int? DecimalPlaces = null)
{
	internal static SliderWidgetReadout Empty { get; } = new(false, 0, 0, 0, 0);
}

/// <summary>Wiring a slider needs beyond the config it was built from: the per-field range fallback for
/// whatever the variable does not declare itself, and the singleton-safe handles to read and (through a
/// fresh scope - see <see cref="SliderWidgetSession.WriteVariableAsync" />) write the named
/// variable.</summary>
internal sealed record SliderVariableBinding(
	string Name,
	double Min,
	double Max,
	double Step,
	VariableRegistry Variables,
	IVariableChangeNotifier Notifier,
	IServiceScopeFactory ScopeFactory,
	Guid? WidgetId = null,
	bool IsDefault = false);

internal sealed record SliderDoublePressBinding(
	Guid WidgetId,
	IFolderCache FolderCache,
	IWidgetTriggerService TriggerService,
	IUiTransport UiTransport);

/// <summary>
/// Drives one Slider widget's UI session: follows the bound variable's value and range, holds the user's
/// own value against a refresh for a few seconds after an interaction so the level does not fight their
/// finger, and writes what they land on back to the variable.
///
/// <para>
/// <b>Two-phase construction.</b> The session has to exist before its tree is built, because the tree's
/// <see cref="MacroDeck.Ui.Model.References.UiComponentEvents" /> handlers close over it - and it has to exist
/// after, because <see cref="MacroDeck.Ui.Runtime.UiView.Batch" /> needs the view the tree was built into.
/// <see cref="BuildEvents" /> is called first to get the handlers for <see cref="SliderWidgetView.Build" />,
/// then <see cref="Attach" /> once the resulting <see cref="UiView" /> exists.
/// </para>
/// </summary>
internal sealed class SliderWidgetSession : IUiSession, IOriginAwareUiSession
{
	// The retired client's SYNC_HOLD_MS, moved to the host: how long a value the user just set is trusted
	// over what the next few readings report, so a slow-to-update provider does not visibly snap the level
	// backward while the write makes its way there and back.
	private static readonly TimeSpan _holdDuration = TimeSpan.FromMilliseconds(3000);

	// How close a fresh reading has to be to the held one to count as "the provider caught up". A stepped
	// variable lands exactly on the held value, because the value was snapped onto its own grid before being
	// written; the tolerance is for one with no step, whose continuous value never does. Half a step rather
	// than a whole one: a whole step accepts the *adjacent* grid point, so a reading still reporting the
	// value the user just dragged away from would release the hold and snap the level backward - the one
	// thing the hold exists to prevent.
	private const double _holdTolerance = 1e-6;

	private static readonly TimeSpan _pushMinInterval = TimeSpan.FromMilliseconds(100);

	private readonly UiState<SliderWidgetReadout> _state;
	private readonly IHostLockState _lockState;
	private readonly TimeProvider _timeProvider;
	private readonly SliderVariableBinding? _variable;
	private readonly SliderDoublePressBinding? _doublePress;
	private readonly bool _interactive;
	private readonly CancellationTokenSource _lifetime = new();

	// A variable change and a dispatch are two different threads reaching this session. The view itself is
	// safe against that now, but this session's own decisions are not: the hold below is written by the
	// dispatch and read by whichever thread published the change, and a refresh has to read the hold, the
	// lock state and _disposed as one consistent answer before it decides what to write. IUiSession.Dispatch
	// is only promised never to run concurrently with itself, which says nothing about the publishing
	// thread. Taken before the view's own serialization everywhere, so the two are always acquired in that
	// order.
	private readonly Lock _viewSync = new();

	private readonly Lock _pushSync = new();
	private double _pendingPushValue;
	private bool _pushDirty;
	private Task? _pushTask;
	private DateTimeOffset _lastPushAt = DateTimeOffset.MinValue;

	private bool _pushRunning;

	// Guarded by _viewSync: written on the dispatch thread, read on whichever thread published a change.
	private double? _heldValue;
	private DateTimeOffset? _holdUntil;
	private double _lastMappedValue;

	// Captured inside Dispatch, which never runs concurrently with itself, so the async handler of the
	// same dispatch reads the origin that belongs to it.
	private string? _pendingOriginClientId;

	private UiView? _view;

	/// <param name="isWidgetSurface">Whether this is the interactive Widget surface, as opposed to the
	/// Preview surface, which never accepts interaction and never writes - though it still follows the
	/// variable, so the editor's preview shows the real level.</param>
	/// <param name="variable">Wiring for the bound variable, non-null exactly when the widget's stored
	/// config names one. The variable's own change events are what keep the readout live, on both
	/// surfaces.</param>
	public SliderWidgetSession(
		UiState<SliderWidgetReadout> state,
		IHostLockState lockState,
		TimeProvider timeProvider,
		bool isWidgetSurface,
		SliderVariableBinding? variable,
		SliderDoublePressBinding? doublePress = null)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(lockState);
		ArgumentNullException.ThrowIfNull(timeProvider);

		_state = state;
		_lockState = lockState;
		_timeProvider = timeProvider;
		_variable = variable;
		_doublePress = doublePress;
		_interactive = isWidgetSurface && variable is not null;
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	/// <summary>The event handlers the track node declares, for <see cref="SliderWidgetView.Build" /> to
	/// attach - empty whenever this surface is not interactive (the Preview surface, or a Widget surface with
	/// no widget to own a default variable), which is what makes such a slider inert against a dispatched
	/// event even from a stale or hostile client: with no handler registered under either name,
	/// <see cref="UiView.Dispatch" /> ignores it before any of this session's code runs.</summary>
	internal IReadOnlyList<UiEventHandler> BuildEvents()
	{
		if (!_interactive)
		{
			return [];
		}

		List<UiEventHandler> handlers =
		[
			UiEventHandler.On(UiComponentEvents.Adjust, HandleInteraction),
			UiEventHandler.OnAsync(UiComponentEvents.Adjust, HandleAdjustPushAsync),
			UiEventHandler.On(UiComponentEvents.Change, HandleInteraction),
			UiEventHandler.OnAsync(UiComponentEvents.Change, HandleChangePushAsync),
		];

		if (_doublePress is not null)
		{
			handlers.Add(UiEventHandler.On(UiComponentEvents.DoublePress, (UiEventData _) => HandleDoublePress()));
			handlers.Add(UiEventHandler.OnAsync(UiComponentEvents.DoublePress,
				(_, ct) => RunDoublePressFlowAsync(_pendingOriginClientId, ct)));
		}

		return handlers;
	}

	/// <summary>Finishes construction once the tree built from <see cref="BuildEvents" /> exists as a
	/// <see cref="UiView" />, and starts following the bound variable.</summary>
	internal void Attach(UiView view)
	{
		ArgumentNullException.ThrowIfNull(view);

		_view = view;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		if (_variable is not null)
		{
			// A registry lookup is synchronous, so no Task.Run is needed for the first resolve; subscribing
			// before it runs is harmless, since a Changed racing in during that window just repeats the same
			// (idempotent, _viewSync-guarded) resolve.
			_variable.Notifier.Changed += OnVariableChanged;
			RefreshFromVariable();
		}
	}

	public UiTree BuildTree()
	{
		lock (_viewSync)
		{
			return _view!.Tree;
		}
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

	public async ValueTask DisposeAsync()
	{
		if (_variable is not null)
		{
			_variable.Notifier.Changed -= OnVariableChanged;
		}

		await _lifetime.CancelAsync().ConfigureAwait(false);

		Task? pushTask;

		lock (_pushSync)
		{
			pushTask = _pushTask;
		}

		if (pushTask is not null)
		{
			await AwaitQuietly(pushTask).ConfigureAwait(false);
		}

		_lifetime.Dispose();
	}

	private static async Task AwaitQuietly(Task task)
	{
		try
		{
			await task.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
		}
	}

	/// <summary>
	/// The synchronous half of an <c>adjust</c>/<c>change</c> dispatch: validates the payload, refuses while
	/// the host is locked or the write is impossible <b>before</b> touching any state so a locked or
	/// unwritable deck never shows a level that was never actually written, maps the dragged fraction onto
	/// the variable's own grid, writes it and arms the hold. Identical for both event names - see
	/// <see cref="_lastMappedValue" /> for how its result reaches the paired asynchronous handler, which is
	/// where the two names start behaving differently.
	/// </summary>
	private UiEventOutcome HandleInteraction(UiEventData data)
	{
		if (!data.TryGetDouble(out var rawFraction))
		{
			return UiEventOutcome.Rejected("The event payload is not a number.");
		}

		if (_lockState.IsLocked)
		{
			return UiEventOutcome.Rejected("The host is locked.");
		}

		// A variable that is missing, or whose owner declares no write capability, cannot be written at all;
		// refusing here - before the optimistic write below - keeps the thumb from moving and snapping back
		// once the write that would follow never happens.
		if (!CanWriteVariable())
		{
			return UiEventOutcome.Rejected("The variable cannot be written.");
		}

		var readout = _state.Peek();

		if (readout.Max <= readout.Min)
		{
			return UiEventOutcome.Rejected("The range is not usable.");
		}

		var mapped = MapToGrid(Math.Clamp(rawFraction, 0, 1), readout);

		using (_view!.Batch())
		{
			_state.Value = readout with { Value = mapped };
		}

		// Armed for both events, including a commit-on-release variable's adjust that will push nothing below:
		// every reading between now and the eventual change is otherwise a provider value that contradicts the
		// level the user is still dragging.
		_heldValue = mapped;
		_holdUntil = _timeProvider.GetUtcNow() + _holdDuration;

		// Captured synchronously here, before this method returns: the paired async handler for the same
		// event name is invoked immediately afterward within this same (never-concurrent, per IUiSession.
		// Dispatch) call, so it reads this before any later dispatch could overwrite it.
		_lastMappedValue = mapped;

		return UiEventOutcome.Accepted;
	}

	private UiEventOutcome HandleDoublePress()
	{
		return _lockState.IsLocked ? UiEventOutcome.Rejected("The host is locked.") : UiEventOutcome.Accepted;
	}

	private async Task RunDoublePressFlowAsync(string? originClientId, CancellationToken cancellationToken)
	{
		Task? push;

		lock (_pushSync)
		{
			push = _pushTask;
		}

		// The second tap's write is still in flight and must land before the flow, even when it fails.
		if (push is not null)
		{
			await Task.WhenAny(push).ConfigureAwait(false);
		}

		lock (_viewSync)
		{
			_heldValue = null;
			_holdUntil = null;
		}

		var binding = _doublePress!;
		var widget = binding.FolderCache.GetAllFolders()
			.SelectMany(folder => folder.Widgets)
			.FirstOrDefault(candidate => candidate.Id == binding.WidgetId);

		if (widget is null)
		{
			return;
		}

		var dispatch = await binding.TriggerService
			.ExecuteAsync(widget,
				WidgetTriggerTypes.DoublePress,
				originClientId,
				originDeviceId: null,
				cancellationToken)
			.ConfigureAwait(false);

		if (dispatch.Result is not { } result || string.IsNullOrEmpty(originClientId))
		{
			return;
		}

		var evt = ActionExecutionDtoMapper.ToStatusEvent(result, widget.Id.ToString(), WidgetTriggerTypes.DoublePress);

		if (!ActionExecutionDtoMapper.IsSuccess(evt.Status))
		{
			await binding.UiTransport.SendToGroup(UiClientGroups.For(originClientId), evt, cancellationToken)
				.ConfigureAwait(false);
		}
	}

	private bool CanWriteVariable() => Entity()?.CanWrite == true;

	private VariableEntity? Entity()
		=> _variable is null
			? null
			: SliderDefaultVariable.Find(_variable.Variables,
				_variable.WidgetId,
				_variable.IsDefault ? null : _variable.Name);

	/// <summary>
	/// Snaps <paramref name="fraction" /> onto the bound variable's own grid, <b>anchored at
	/// <see cref="SliderWidgetReadout.Min" /></b> - the retired client anchored at zero
	/// (<c>round(raw / step) * step</c>), which lands off that grid whenever <c>Min</c> is not itself
	/// a multiple of the step: min 3, max 103, step 10 gives 50 that way and 53 this way.
	///
	/// <para>
	/// Ties round away from zero rather than through <see cref="Math.Round(double)" />'s banker's rounding,
	/// because the client snapping the same level with JavaScript's half-up <c>Math.round</c> has to reach
	/// the same grid point.
	/// </para>
	///
	/// <para>
	/// The step is ignored under exactly the condition that keeps it off the wire, so the two sides agree on
	/// whether there is a grid at all: a client sent no <c>step</c> snaps nothing, and a host that snapped
	/// anyway would answer the drag with a value the client never offered.
	/// </para>
	/// </summary>
	private static double MapToGrid(double fraction, SliderWidgetReadout readout)
	{
		var raw = readout.Min + (fraction * (readout.Max - readout.Min));

		if (SliderWidgetView.ComputeStepFraction(readout) is null)
		{
			return raw;
		}

		var steps = Math.Round((raw - readout.Min) / readout.Step, MidpointRounding.AwayFromZero);
		var onGrid = readout.Min + (steps * readout.Step);

		// Rebuilding the value by multiplication reintroduces binary-float noise - 7 * 0.1 is
		// 0.7000000000000001 - and this value is both what the variable is written with and what the readout
		// beside the track spells out for the user. Twelve places is far below any slider's real precision
		// and far above the noise.
		return Math.Clamp(Math.Round(onGrid, 12), readout.Min, readout.Max);
	}

	private Task HandleAdjustPushAsync(CancellationToken cancellationToken)
	{
		// Issue #469: an adjust on a variable whose write capability commits on release must not write, but
		// the readout already followed the finger above - only the push is withheld.
		if (Entity()?.CommitOnRelease == true)
		{
			return Task.CompletedTask;
		}

		return RequestPushAsync(LastMappedValue());
	}

	private Task HandleChangePushAsync(CancellationToken cancellationToken) => RequestPushAsync(LastMappedValue());

	private double LastMappedValue()
	{
		lock (_viewSync)
		{
			return _lastMappedValue;
		}
	}

	/// <summary>
	/// Coalesces concurrent push requests into a minimum interval: a client that dispatches far faster than
	/// the 150ms the retired client throttled to must still only reach the variable once every
	/// <see cref="_pushMinInterval" />, and the value it lands on is never dropped. A request arriving while
	/// one is already in flight does not queue a second push behind it - it marks the running one
	/// <see cref="_pushDirty" /> instead, so that one push loops around for exactly one more trailing round
	/// once the current write returns, carrying whatever is newest by then. Every caller, including one that
	/// only marked the loop dirty, awaits the same task and so only completes once its own value (or a newer
	/// one that superseded it) has actually been written - never once it was merely queued behind a write
	/// already on the wire.
	/// </summary>
	private Task RequestPushAsync(double value)
	{
		lock (_pushSync)
		{
			_pendingPushValue = value;
			_pushDirty = true;

			if (_pushRunning)
			{
				return _pushTask!;
			}

			// An explicit flag rather than Task.IsCompleted: the loop releases _pushSync at its last exit
			// before the async state machine completes the task, so a caller landing in that window would
			// see "still running", return the finished task, and have its value silently dropped - and when
			// that caller is the change handler, the value the user landed on is the one that never arrives.
			_pushRunning = true;

			// Task.Run so the loop never runs on the dispatch thread: its first leg has no await when the
			// interval has already elapsed, which would otherwise put a write inside this lock.
			_pushTask = Task.Run(() => RunCoalescedPushAsync(_lifetime.Token), CancellationToken.None);

			return _pushTask;
		}
	}

	private async Task RunCoalescedPushAsync(CancellationToken cancellationToken)
	{
		try
		{
			await PushLoopAsync(cancellationToken).ConfigureAwait(false);
		}
		finally
		{
			// Only the cancelled and faulted exits reach here still marked as running: the normal exit
			// clears the flag inside the same locked region that decides to stop, because anything less is
			// the dropped-value window above.
			lock (_pushSync)
			{
				_pushRunning = false;
			}
		}
	}

	private async Task PushLoopAsync(CancellationToken cancellationToken)
	{
		while (true)
		{
			var wait = _pushMinInterval - (_timeProvider.GetUtcNow() - _lastPushAt);

			if (wait > TimeSpan.Zero)
			{
				try
				{
					await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}

			double value;

			lock (_pushSync)
			{
				value = _pendingPushValue;
				_pushDirty = false;
				_lastPushAt = _timeProvider.GetUtcNow();
			}

			await WriteVariableAsync(value).ConfigureAwait(false);

			lock (_pushSync)
			{
				if (!_pushDirty)
				{
					_pushRunning = false;

					return;
				}
			}
		}
	}

	private async Task WriteVariableAsync(double value)
	{
		var entity = Entity();

		if (entity is null || !entity.CanWrite)
		{
			return;
		}

		// IVariableService is scoped, and this session is held for the widget's whole lifetime, so it must
		// not capture one - a fresh scope is created for each write instead, mirroring UserVariableWriter.
		await using var scope = _variable!.ScopeFactory.CreateAsyncScope();
		var service = scope.ServiceProvider.GetRequiredService<IVariableService>();

		await service.SetValue(entity.Id, value).ConfigureAwait(false);
	}

	private void OnVariableChanged(object? sender, VariableChangedEventArgs args)
	{
		if (string.Equals(args.Name, _variable!.Name, StringComparison.Ordinal))
		{
			RefreshFromVariable();
		}
	}

	/// <summary>Republishes the readout from the bound variable. Runs on whatever thread published the
	/// variable change, so it is guarded the same way a dispatch is.</summary>
	private void RefreshFromVariable()
	{
		lock (_viewSync)
		{
			using (_view!.Batch())
			{
				_state.Value = ReadReadout();
			}
		}
	}

	/// <summary>Reads everything the track is drawn against out of the bound variable in one go: its value,
	/// resolved against the hold; its own volatile range where it declares one and the widget config's
	/// fallback per field where it does not; and the static attributes the readout text is formatted with.
	/// A variable that is missing, unavailable or not a number yields <c>Found = false</c> at the range's own
	/// minimum - a defined, never NaN/blank fallback. Must run under <see cref="_viewSync" />, which both
	/// callers already hold.</summary>
	private SliderWidgetReadout ReadReadout()
	{
		var binding = _variable!;
		var entity = Entity();

		// A provider's declared bound is an unvalidated double, and a NaN one would reach UiCanonicalJson,
		// whose Strict number handling throws rather than writing it - faulting the whole session over one
		// bad division inside an integration. A non-finite bound falls back exactly as an absent one does.
		var min = Bound(entity?.Min, binding.Min);
		var max = Bound(entity?.Max, binding.Max);
		var step = Bound(entity?.Step, binding.Step);

		var parsed = entity is not null && binding.Variables.IsAvailable(entity.Id)
			? ParseNumeric(entity.Value)
			: null;

		return new SliderWidgetReadout(parsed is not null,
			min,
			max,
			step,
			parsed is { } value ? ResolveAgainstHold(value, step) : min,
			entity?.SemanticKind,
			entity?.Unit,
			entity?.DecimalPlaces);
	}

	private static double Bound(double? declared, double fallback)
		=> declared is { } value && double.IsFinite(value) ? value : fallback;

	/// <summary>
	/// The hold decision <see cref="HandleInteraction" />'s own comment promises: the value the user just
	/// dragged to wins over whatever a fresh reading reports next, until the fresh value lands within half a
	/// step of it (the provider caught up) or the hold's window elapses, at which point it releases and the
	/// fresh value is adopted normally. Must run under <see cref="_viewSync" />, which every caller already
	/// holds.
	/// </summary>
	private double ResolveAgainstHold(double freshValue, double step)
	{
		if (_holdUntil is { } deadline && _heldValue is { } held)
		{
			var closeEnough = Math.Abs(freshValue - held) <= Math.Max(step / 2, _holdTolerance);
			var expired = _timeProvider.GetUtcNow() >= deadline;

			if (closeEnough || expired)
			{
				_holdUntil = null;
				_heldValue = null;
			}
			else
			{
				return held;
			}
		}

		return freshValue;
	}

	private static double? ParseNumeric(string raw)
		=> double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
			double.IsFinite(value)
				? value
				: null;
}
