using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Entities;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.MusicPlayer;

/// <summary>
/// Keeps one Music Player view in step with the host's own picture of its player.
///
/// <para>
/// <b>It listens rather than polls.</b> The music player background service already reads every provider
/// on its own cadence and pushes what changed; this session hangs off the in-process notifier beside that
/// push, so a widget costs no second read of anything - the same arrangement <c>WeatherWidgetSession</c>
/// has, and the reason <see cref="IMusicPlayerStateNotifier" /> exists.
/// </para>
///
/// <para>
/// <b>It declares no events.</b> The press that runs the widget's flows belongs to the deck tile around
/// this tree, exactly as it did before the migration - see <see cref="MusicPlayerWidgetView" />.
/// </para>
/// </summary>
internal sealed class MusicPlayerWidgetSession : IUiSession
{
	private readonly UiView _view;
	private readonly UiState<MusicPlayerViewState> _state;
	private readonly UiState<MusicPlayerWidgetData> _config;
	private readonly MusicPlayerViewStateResolver _resolver;
	private readonly IMusicPlayerStateNotifier _notifier;
	private readonly IWidgetRenderSignals _renderSignals;
	private readonly string? _widgetId;
	private readonly ILogger _logger;

	private IDisposable? _dataSignalSubscription;

	// A refresh raised from the broadcast service's thread and a client's own dispatch both reach this
	// session. The view is safe against that on its own; what needs the lock is _disposed, which the refresh
	// re-reads after its asynchronous resolve so a pass in flight when the session went away cannot write
	// into a view nobody drains again - the same reasoning SliderWidgetSession's _viewSync states.
	private readonly Lock _viewSync = new();
	private bool _disposed;

	// Resolution is asynchronous (a cover may have to be fetched), so a burst of state changes has to
	// coalesce into one pass rather than starting a fetch each - the shape ActionButtonWidgetSession uses
	// for its icon resolution, with no payload to carry across passes since a pass always means "resolve
	// whatever is true now".
	private readonly Lock _refreshSync = new();
	private bool _refreshRunning;
	private bool _refreshDirty;
	private Task? _refreshTask;

	private readonly CancellationTokenSource _lifetime = new();

	/// <param name="widgetId">The stored widget this session draws, or <c>null</c> for a Preview, which
	/// has none. A Preview needs no stored-data subscription either: the editor reopens its session on
	/// every keystroke, so its draft configuration already arrives that way.</param>
	public MusicPlayerWidgetSession(
		UiView view,
		UiState<MusicPlayerViewState> state,
		UiState<MusicPlayerWidgetData> config,
		MusicPlayerViewStateResolver resolver,
		IMusicPlayerStateNotifier notifier,
		IWidgetRenderSignals renderSignals,
		string? widgetId,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(config);
		ArgumentNullException.ThrowIfNull(resolver);
		ArgumentNullException.ThrowIfNull(notifier);
		ArgumentNullException.ThrowIfNull(renderSignals);
		ArgumentNullException.ThrowIfNull(logger);

		_view = view;
		_state = state;
		_config = config;
		_resolver = resolver;
		_notifier = notifier;
		_renderSignals = renderSignals;
		_widgetId = widgetId;
		_logger = logger.ForContext<MusicPlayerWidgetSession>();

		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_notifier.StateChanged += OnStateChanged;
		_notifier.InstancesChanged += OnInstancesChanged;

		if (_widgetId is not null)
		{
			_dataSignalSubscription = _renderSignals.SubscribeDataChanged(_widgetId, OnDataChanged);
		}
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree()
	{
		lock (_viewSync)
		{
			return _view.Tree;
		}
	}

	public IReadOnlyList<UiPatch> DrainPatches()
	{
		lock (_viewSync)
		{
			return _view.DrainPatches();
		}
	}

	public void Dispatch(UiEvent uiEvent)
	{
		lock (_viewSync)
		{
			_view.Dispatch(uiEvent);
		}
	}

	/// <summary>Awaits whatever refresh is currently in flight, or returns immediately when none is.
	/// Internal rather than private so a test can settle deterministically on work this session starts
	/// fire-and-forget - the same reason <c>ActionButtonWidgetSession</c> exposes
	/// <c>WaitForIconResolutionAsync</c>.</summary>
	internal Task WaitForRefreshAsync()
	{
		Task? task;

		lock (_refreshSync)
		{
			task = _refreshTask;
		}

		return task ?? Task.CompletedTask;
	}

	public async ValueTask DisposeAsync()
	{
		lock (_viewSync)
		{
			_disposed = true;
		}

		_notifier.StateChanged -= OnStateChanged;
		_notifier.InstancesChanged -= OnInstancesChanged;
		_dataSignalSubscription?.Dispose();

		await _lifetime.CancelAsync().ConfigureAwait(false);

		Task? refresh;

		lock (_refreshSync)
		{
			refresh = _refreshTask;
		}

		if (refresh is not null)
		{
			try
			{
				await refresh.ConfigureAwait(false);
			}
			catch (OperationCanceledException)
			{
			}
		}

		_lifetime.Dispose();
	}

	// Every state change refreshes, not only the configured instance's: which instance this widget shows
	// depends on the whole list (an unset selection follows the first one, and a stale one falls back), so
	// filtering here would need the resolution this refresh is what performs. A refresh that changes
	// nothing re-evaluates a handful of cells and emits no patch at all.
	private void OnStateChanged(object? sender, MusicPlayerStateChangedEventArgs args) => RequestRefresh();

	private void OnInstancesChanged(object? sender, EventArgs args) => RequestRefresh();

	/// <summary>
	/// The widget's stored configuration was saved. A widget session is shared and long-lived, so
	/// without this an editor change - a cover style, a hidden row, a different player - would sit
	/// unapplied on the deck until something else happened to reopen the session, which is what made a
	/// freshly saved "full cover" come back as the small one on the next start.
	/// </summary>
	private void OnDataChanged(WidgetEntity widget)
	{
		var updated = MusicPlayerWidgetData.Parse(MusicPlayerWidgetData.ParseData(widget.Data));

		lock (_viewSync)
		{
			if (_disposed)
			{
				return;
			}

			// A record, so an unrelated save - a moved widget, a border change - writes an equal value
			// and emits nothing at all.
			_config.Value = updated;
		}

		// The instance selection lives in the configuration too, so the state has to be resolved against
		// the new one rather than left showing the player the old selection pointed at.
		RequestRefresh();
	}

	private void RequestRefresh()
	{
		lock (_refreshSync)
		{
			if (_refreshRunning)
			{
				_refreshDirty = true;

				return;
			}

			_refreshRunning = true;
			_refreshTask = RefreshLoopAsync();
		}
	}

	private async Task RefreshLoopAsync()
	{
		// Off the caller's thread before anything else: the caller is the broadcast service's own tick,
		// and a cover fetch must never hold it up.
		await Task.Yield();

		try
		{
			while (true)
			{
				await RefreshOnceAsync().ConfigureAwait(false);

				lock (_refreshSync)
				{
					if (!_refreshDirty)
					{
						_refreshRunning = false;
						_refreshTask = null;

						return;
					}

					_refreshDirty = false;
				}
			}
		}
		catch (OperationCanceledException)
		{
			lock (_refreshSync)
			{
				_refreshRunning = false;
				_refreshTask = null;
			}
		}
	}

	private async Task RefreshOnceAsync()
	{
		MusicPlayerViewState previous;

		lock (_viewSync)
		{
			if (_disposed)
			{
				return;
			}

			previous = _state.Peek();
		}

		MusicPlayerViewState resolved;

		try
		{
			resolved = await _resolver.ResolveAsync(_config.Peek(), previous, _lifetime.Token)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
#pragma warning disable CA1031 // A provider is plugin-owned; its fault must leave a usable widget.
		catch (Exception exception)
#pragma warning restore CA1031
		{
			_logger.Error(exception, "Failed to resolve the Music Player widget's state");

			return;
		}

		lock (_viewSync)
		{
			// Re-checked under the lock: the resolve above ran outside it, and the session may have been
			// disposed while it did - writing then would touch a view nobody drains again.
			if (_disposed)
			{
				return;
			}

			_state.Value = resolved;
		}
	}
}
