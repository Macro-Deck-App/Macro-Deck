using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeck.Ui.Widgets;
using MacroDeckHost.Integrations.AdGuardHome.Connections;
using MacroDeckHost.Integrations.AdGuardHome.State;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.AdGuardHome;

namespace MacroDeckHost.Integrations.AdGuardHome.Widgets;

internal sealed class AdGuardHomeWidgetSession : IUiSession
{
	public static readonly TimeSpan FailureDisplay = TimeSpan.FromSeconds(10);

	private static readonly TimeSpan _countdownTick = TimeSpan.FromSeconds(15);

	private readonly AdGuardHomeWidgetOptions _options;
	private readonly IAdGuardHomeInstances _instances;
	private readonly TimeProvider _time;
	private readonly UiState<AdGuardHomeViewState> _state;
	private readonly UiState<AdGuardHomeIconResources> _icons;
	private readonly UiView _view;
	private readonly ITimer _timer;
	private readonly Lock _sync = new();

	private Func<IUiResourceRegistry?>? _pendingIcons;
	private IUiEventOrigin? _origin;
	private LocalizedText _failure;
	private DateTimeOffset _failureUntil;
	private bool _disposed;

	public AdGuardHomeWidgetSession(
		UiSurface surface,
		AdGuardHomeWidgetOptions options,
		IAdGuardHomeInstances instances,
		TimeProvider time,
		AdGuardHomeIconResources icons,
		Func<IUiResourceRegistry?>? pendingIcons,
		string? backgroundColor)
	{
		ArgumentNullException.ThrowIfNull(surface);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(instances);

		_options = options;
		_instances = instances;
		_time = time;
		_pendingIcons = pendingIcons;
		_icons = new UiState<AdGuardHomeIconResources>(icons);
		_state = new UiState<AdGuardHomeViewState>(Resolve());
		_view = new UiView(surface,
			AdGuardHomeWidgetView.Build(_state,
				options,
				_icons,
				new AdGuardHomeWidgetCommands(id => Press(new AdGuardHomeCommand(AdGuardHomeCommandKind.PauseProtection,
						AdGuardHomeWidgetType.Duration(id)?.Length)),
					() => Press(new AdGuardHomeCommand(AdGuardHomeCommandKind.EnableProtection))),
				UiWidgetSafeArea.RadiusOf(surface),
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
		Deliver(uiEvent, null);
	}

	public void Dispatch(UiEvent uiEvent, IUiEventOrigin origin)
	{
		Deliver(uiEvent, origin);
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

	private void Deliver(UiEvent uiEvent, IUiEventOrigin? origin)
	{
		lock (_sync)
		{
			_origin = origin;
			try
			{
				_view.Dispatch(uiEvent);
			}
			finally
			{
				_origin = null;
			}
		}
	}

	private void OnInstancesChanged(object? sender, EventArgs args)
	{
		Refresh();
	}

	private UiEventOutcome Press(AdGuardHomeCommand command)
	{
		var entryId = _options.InstanceId ?? (_instances.Instances is [var first, ..] ? first.EntryId : null);
		if (entryId is null)
		{
			return UiEventOutcome.Rejected("No AdGuard Home instance is configured.");
		}

		_ = RunAsync(entryId, command, _origin);
		return UiEventOutcome.Accepted;
	}

	private async Task RunAsync(string entryId, AdGuardHomeCommand command, IUiEventOrigin? origin)
	{
		var outcome = await _instances.ExecuteAsync(entryId, command, CancellationToken.None).ConfigureAwait(false);
		if (outcome == AdGuardHomeCommandOutcome.Succeeded)
		{
			return;
		}

		LocalizedText message = outcome switch
		{
			AdGuardHomeCommandOutcome.NotFound => Strings.InstanceNotFound(),
			AdGuardHomeCommandOutcome.Unauthorized => Strings.Unauthorized(),
			AdGuardHomeCommandOutcome.Timeout => Strings.Timeout(),
			AdGuardHomeCommandOutcome.Incompatible => Strings.Incompatible(),
			AdGuardHomeCommandOutcome.Redirected => Strings.Redirected(),
			AdGuardHomeCommandOutcome.Unreachable => Strings.Offline(),
			_ => Strings.CommandFailed()
		};

		lock (_sync)
		{
			_failure = message;
			_failureUntil = _time.GetUtcNow() + FailureDisplay;
		}

		Refresh();

		if (origin is not null)
		{
			await origin.ReportFailureAsync(message).ConfigureAwait(false);
		}
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

			if (next.Protection == AdGuardHomeProtection.Paused
				&& (delay == Timeout.InfiniteTimeSpan || delay > _countdownTick))
			{
				delay = _countdownTick;
			}

			_timer.Change(delay, Timeout.InfiniteTimeSpan);

			if (_pendingIcons?.Invoke() is { } registry)
			{
				_pendingIcons = null;
				_ = ResolveIconsAsync(registry);
			}
		}
	}

	// A deck can open the widget before the integration finished initializing, when no icons can be
	// resolved yet; the first refresh after initialization fills them in.
	private async Task ResolveIconsAsync(IUiResourceRegistry registry)
	{
		var icons = await AdGuardHomeWidgetIcons.ResolveAsync(registry, CancellationToken.None).ConfigureAwait(false);

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			using (_view.Batch())
			{
				_icons.Set(icons);
			}
		}
	}
}
