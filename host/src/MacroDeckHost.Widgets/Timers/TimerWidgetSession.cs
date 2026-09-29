using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Timers;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.Timers;

internal sealed class TimerWidgetSession : IUiSession, IOriginAwareUiSession
{
	private readonly Guid _widgetId;
	private readonly TimerWidgetConfig _config;
	private readonly TimerWidgetCoordinator _timers;
	private readonly IHostLockState _lockState;
	private readonly IUiTransport _transport;
	private readonly UiState<TimerFace> _face;
	private readonly UiView _view;
	private readonly Lock _sync = new();

	private string? _pendingOriginClientId;
	private bool _disposed;

	public TimerWidgetSession(UiSurface surface,
		Guid widgetId,
		TimerWidgetConfig config,
		TimerWidgetSettings settings,
		TimerWidgetCoordinator timers,
		IHostLockState lockState,
		IUiTransport transport)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(settings);

		_widgetId = widgetId;
		_config = config;
		_timers = timers;
		_lockState = lockState;
		_transport = transport;
		_face = new UiState<TimerFace>(CurrentFace());

		IReadOnlyList<UiEventHandler> events =
		[
			UiEventHandler.On(UiComponentEvents.Press, _ => Press(TimerGesture.Press)),
			UiEventHandler.On(UiComponentEvents.LongPress, _ => Press(TimerGesture.LongPress)),
		];

		_view = new UiView(surface,
			TimerWidgetView.Build(_face, settings, WidgetSafeArea.RadiusOf(surface), events));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_timers.Store.Changed += OnTimerChanged;

		// A transition between reading the first face and subscribing above is not lost.
		OnTimerChanged(null, new TimerWidgetChangedEventArgs(_widgetId));
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => Dispatch(uiEvent, null);

	public void Dispatch(UiEvent uiEvent, string? originClientId)
	{
		lock (_sync)
		{
			_pendingOriginClientId = originClientId;
			_view.Dispatch(uiEvent);
		}
	}

	public ValueTask DisposeAsync()
	{
		_timers.Store.Changed -= OnTimerChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private UiEventOutcome Press(TimerGesture gesture)
	{
		var originClientId = _pendingOriginClientId;

		if (_lockState.IsLocked)
		{
			EmitRefusal(originClientId, gesture);

			return UiEventOutcome.Rejected("The host is locked.");
		}

		_timers.Post(_widgetId, gesture, originClientId, originDeviceId: null);

		return UiEventOutcome.Accepted;
	}

	private void EmitRefusal(string? originClientId, TimerGesture gesture)
	{
		if (string.IsNullOrEmpty(originClientId))
		{
			return;
		}

		var evt = new ActionExecutionStatusEvent
		{
			ExecutionId = Guid.NewGuid().ToString(),
			Status = ActionExecutionStatus.Failed,
			WidgetId = _widgetId.ToString(),
			TriggerType = gesture == TimerGesture.Press ? WidgetTriggerTypes.ShortPress : WidgetTriggerTypes.LongPress,
			Error = new TransportError
				{ Code = ActionExecutionErrorCodes.HostLocked, Message = AppStrings.Errors.Common.HostLocked() }
		};

		_ = _transport.SendToGroup(UiClientGroups.For(originClientId), evt);
	}

	private void OnTimerChanged(object? sender, TimerWidgetChangedEventArgs args)
	{
		if (args.WidgetId != _widgetId)
		{
			return;
		}

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var face = CurrentFace();

			if (face != _face.Value)
			{
				using (_view.Batch())
				{
					_face.Set(face);
				}
			}
		}
	}

	private TimerFace CurrentFace()
	{
		var now = _timers.Store.Now;

		return _timers.Store.Get(_widgetId) is { } snapshot
			? TimerFace.From(snapshot, now)
			: TimerFace.Idle(_config, now);
	}
}
