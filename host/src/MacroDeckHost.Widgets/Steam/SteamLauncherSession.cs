using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Steam;
using MacroDeckHost.Application.Ui.Sessions.InProcess;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Actions;
using MacroDeckHost.Domain.Common;
using MacroDeckHost.Localization;
using Errors = MacroDeckHost.Localization.AppStrings.Integrations.Steam.Errors;

namespace MacroDeckHost.Widgets.Steam;

internal sealed class SteamLauncherSession : IUiSession, IOriginAwareUiSession
{
	public static readonly TimeSpan FailureDisplay = TimeSpan.FromSeconds(10);

	private readonly Guid? _widgetId;
	private readonly ISteamSource? _source;
	private readonly SteamLauncherOptions _options;
	private readonly SteamArtworkCache _artwork;
	private readonly UiResource? _glyph;
	private readonly IHostLockState _lockState;
	private readonly IUiTransport _transport;
	private readonly TimeProvider _time;
	private readonly UiState<SteamLauncherState> _state;
	private readonly UiView _view;
	private readonly ITimer _timer;
	private readonly Lock _sync = new();

	private string? _pendingOriginClientId;
	private LocalizedText _failure;
	private DateTimeOffset _failureUntil;
	private bool _disposed;

	public SteamLauncherSession(
		UiSurface surface,
		Guid? widgetId,
		ISteamSource? source,
		SteamLauncherOptions options,
		SteamArtworkCache artwork,
		UiResource? glyph,
		IHostLockState lockState,
		IUiTransport transport,
		TimeProvider time,
		Func<UiState<SteamLauncherState>, Func<int, UiEventOutcome>, UiElement> build)
	{
		_widgetId = widgetId;
		_source = source;
		_options = options;
		_artwork = artwork;
		_glyph = glyph;
		_lockState = lockState;
		_transport = transport;
		_time = time;
		_state = new UiState<SteamLauncherState>(Compute());
		_view = new UiView(surface, build(_state, Press));
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_timer = time.CreateTimer(_ => Refresh(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

		if (_source is not null)
		{
			_source.Changed += OnChanged;
		}

		_artwork.Changed += OnChanged;
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
		if (_source is not null)
		{
			_source.Changed -= OnChanged;
		}

		_artwork.Changed -= OnChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		_timer.Dispose();
		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private void OnChanged(object? sender, EventArgs args) => Refresh();

	private UiEventOutcome Press(int appId)
	{
		var originClientId = _pendingOriginClientId;

		if (_lockState.IsLocked)
		{
			Emit(originClientId, ActionExecutionErrorCodes.HostLocked, AppStrings.Errors.Common.HostLocked());
			return UiEventOutcome.Rejected("The host is locked.");
		}

		if (_source is not { IsAvailable: true })
		{
			return UiEventOutcome.Rejected("Steam is not available.");
		}

		_ = RunAsync(appId, originClientId);
		return UiEventOutcome.Accepted;
	}

	private async Task RunAsync(int appId, string? originClientId)
	{
		var outcome = await _source!.ExecuteAsync(new SteamCommand(SteamCommandKind.LaunchGame, appId),
				CancellationToken.None)
			.ConfigureAwait(false);

		if (outcome == SteamCommandOutcome.Succeeded)
		{
			return;
		}

		LocalizedString message = outcome switch
		{
			SteamCommandOutcome.NotAvailable => Errors.NotAvailable(),
			SteamCommandOutcome.InvalidAppId => Errors.InvalidGame(),
			_ => Errors.LaunchFailed()
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

	private SteamLauncherState Compute()
	{
		var failure = _time.GetUtcNow() < _failureUntil ? _failure : default;
		return SteamWidgetStates.Launcher(_source, _options, _artwork, _glyph, failure);
	}

	private void Refresh()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var next = Compute();
			if (next != _state.Value)
			{
				using (_view.Batch())
				{
					_state.Set(next);
				}
			}

			var now = _time.GetUtcNow();
			_timer.Change(now < _failureUntil ? _failureUntil - now : Timeout.InfiniteTimeSpan,
				Timeout.InfiniteTimeSpan);
		}
	}
}
