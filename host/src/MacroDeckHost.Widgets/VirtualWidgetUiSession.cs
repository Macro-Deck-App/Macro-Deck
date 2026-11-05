using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Profiles;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Domain.Common;

namespace MacroDeckHost.Widgets;

internal sealed class VirtualWidgetUiSession : IUiSession, IOriginAwareUiSession
{
	private readonly string _widgetId;
	private readonly VirtualWidgetPress _press;
	private readonly IHostLockState _lockState;
	private readonly UiView _view;
	private readonly Lock _sync = new();

	private string? _pendingOriginClientId;

	public VirtualWidgetUiSession(
		UiSurface surface,
		string widgetId,
		IReadOnlyDictionary<string, string> triggerTypeByEventName,
		VirtualWidgetPress press,
		IHostLockState lockState,
		Func<IReadOnlyList<UiEventHandler>, UiElement> build)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(triggerTypeByEventName);
		ArgumentNullException.ThrowIfNull(build);

		_widgetId = widgetId;
		_press = press;
		_lockState = lockState;

		var events = new List<UiEventHandler>();

		foreach (var (eventName, triggerType) in triggerTypeByEventName)
		{
			events.Add(UiEventHandler.On(eventName, (UiEventData _) => Accept(triggerType)));
			events.Add(UiEventHandler.OnAsync(eventName, (_, ct) => RouteAsync(triggerType, ct)));
		}

		_view = new UiView(surface, build(events));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree()
	{
		return _view.Tree;
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		return _view.DrainPatches();
	}

	public void Dispatch(UiEvent uiEvent)
	{
		Dispatch(uiEvent, null);
	}

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
		_view.Dispose();
		return ValueTask.CompletedTask;
	}

	private UiEventOutcome Accept(string triggerType)
	{
		if (!_lockState.IsLocked)
		{
			return UiEventOutcome.Accepted;
		}

		_press.ReportLocked(_widgetId, triggerType, _pendingOriginClientId);
		return UiEventOutcome.Rejected("The host is locked.");
	}

	private Task RouteAsync(string triggerType, CancellationToken cancellationToken)
	{
		return _press.RouteAsync(_widgetId, triggerType, _pendingOriginClientId, cancellationToken);
	}
}

internal static class VirtualWidgetGestures
{
	public static IReadOnlyDictionary<string, string> PressAndLongPress { get; } = new Dictionary<string, string>
	{
		[UiComponentEvents.Press] = WidgetTriggerTypes.ShortPress,
		[UiComponentEvents.LongPress] = WidgetTriggerTypes.LongPress
	};
}
