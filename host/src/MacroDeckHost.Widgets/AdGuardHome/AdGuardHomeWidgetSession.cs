using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.AdGuardHome;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Localization;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.AdGuardHome;

namespace MacroDeckHost.Widgets.AdGuardHome;

internal sealed class AdGuardHomeWidgetSession : IUiSession, IOriginAwareUiSession
{
	public static readonly TimeSpan FailureDisplay = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan _countdownTick = TimeSpan.FromSeconds(15);

	private readonly Guid? _widgetId;
	private readonly AdGuardHomeWidgetOptions _options;
	private readonly IAdGuardHomeInstances _instances;
	private readonly IHostLockState _lockState;
	private readonly IUiTransport _transport;
	private readonly TimeProvider _time;
	private readonly UiState<AdGuardHomeViewState> _state;
	private readonly UiView _view;
	private readonly ITimer _timer;
	private readonly Lock _sync = new();

	private string? _pendingOriginClientId;
	private LocalizedText _failure;
	private DateTimeOffset _failureUntil;
	private bool _disposed;

	public AdGuardHomeWidgetSession(
		UiSurface surface,
		Guid? widgetId,
		AdGuardHomeWidgetOptions options,
		IAdGuardHomeInstances instances,
		IHostLockState lockState,
		IUiTransport transport,
		TimeProvider time,
		AdGuardHomeIconResources icons,
		string? backgroundColor)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(instances);

		_widgetId = widgetId;
		_options = options;
		_instances = instances;
		_lockState = lockState;
		_transport = transport;
		_time = time;
		_state = new UiState<AdGuardHomeViewState>(Resolve());
		_view = new UiView(surface,
			AdGuardHomeWidgetView.Build(_state,
				options,
				icons,
				new AdGuardHomeWidgetCommands(
					id => Press(new AdGuardHomeCommand(AdGuardHomeCommandKind.PauseProtection,
						AdGuardHomeWidgetType.Duration(id)?.Length)),
					() => Press(new AdGuardHomeCommand(AdGuardHomeCommandKind.EnableProtection))),
				WidgetSafeArea.RadiusOf(surface),
				backgroundColor));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_timer = time.CreateTimer(_ => Refresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
		_instances.Changed += OnInstancesChanged;

		Refresh();
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
		_instances.Changed -= OnInstancesChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		_timer.Dispose();
		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private void OnInstancesChanged(object? sender, EventArgs args) => Refresh();

	private UiEventOutcome Press(AdGuardHomeCommand command)
	{
		var originClientId = _pendingOriginClientId;

		if (_lockState.IsLocked)
		{
			Emit(originClientId, ActionExecutionErrorCodes.HostLocked, AppStrings.Errors.Common.HostLocked());
			return UiEventOutcome.Rejected("The host is locked.");
		}

		var entryId = _options.InstanceId ?? (_instances.Instances is [var first, ..] ? first.EntryId : null);
		if (entryId is null)
		{
			return UiEventOutcome.Rejected("No AdGuard Home instance is configured.");
		}

		_ = RunAsync(entryId, command, originClientId);
		return UiEventOutcome.Accepted;
	}

	private async Task RunAsync(string entryId, AdGuardHomeCommand command, string? originClientId)
	{
		var outcome = await _instances.ExecuteAsync(entryId, command, CancellationToken.None).ConfigureAwait(false);
		if (outcome == AdGuardHomeCommandOutcome.Succeeded)
		{
			return;
		}

		LocalizedString message = outcome switch
		{
			AdGuardHomeCommandOutcome.NotFound => Strings.InstanceNotFound(),
			AdGuardHomeCommandOutcome.Unauthorized => Strings.Unauthorized(),
			AdGuardHomeCommandOutcome.Timeout => Strings.Timeout(),
			AdGuardHomeCommandOutcome.Incompatible => Strings.Incompatible(),
			AdGuardHomeCommandOutcome.Redirected => Strings.Redirected(),
			AdGuardHomeCommandOutcome.Unreachable => Strings.Offline(),
			_ => Strings.CommandFailed(),
		};

		lock (_sync)
		{
			_failure = message;
			_failureUntil = _time.GetUtcNow() + FailureDisplay;
		}

		Emit(originClientId, ActionExecutionErrorCodes.ActionFailed, message);
		Refresh();
	}

	private void Emit(string? originClientId, string code, LocalizedString message)
	{
		if (string.IsNullOrEmpty(originClientId))
		{
			return;
		}

		var evt = new ActionExecutionStatusEvent
		{
			ExecutionId = Guid.NewGuid().ToString(),
			Status = ActionExecutionStatus.Failed,
			WidgetId = _widgetId?.ToString(),
			TriggerType = WidgetTriggerTypes.ShortPress,
			Error = new TransportError { Code = code, Message = message },
		};

		_ = _transport.SendToGroup(UiClientGroups.For(originClientId), evt);
	}

	private AdGuardHomeViewState Resolve()
	{
		var now = _time.GetUtcNow();
		var failure = now < _failureUntil ? _failure : default;
		return AdGuardHomeViewStateResolver.Resolve(_options, _instances, now, failure);
	}

	private void Refresh()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var next = Resolve();
			if (next != _state.Value)
			{
				using (_view.Batch())
				{
					_state.Set(next);
				}
			}

			var now = _time.GetUtcNow();
			var delay = Timeout.InfiniteTimeSpan;
			if (now < _failureUntil)
			{
				delay = _failureUntil - now;
			}

			if (next.Protection == AdGuardHomeProtection.Paused && (delay == Timeout.InfiniteTimeSpan || delay > _countdownTick))
			{
				delay = _countdownTick;
			}

			_timer.Change(delay, Timeout.InfiniteTimeSpan);
		}
	}
}
