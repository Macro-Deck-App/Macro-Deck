using MacroDeck.Sdk.Ui;
using MacroDeck.Sdk.Variables;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Integrations.Streaming.Widgets;

internal delegate Task<IVariableHistoryWindow?> StreamStatsHistoryOpener(
	string variableName,
	int capacity,
	CancellationToken cancellationToken);

internal sealed class StreamStatsWidgetSession : IUiSession
{
	public const int HistoryLength = 40;

	internal static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(1);

	private readonly StreamPlatform _platform;
	private readonly UiView _view;
	private readonly UiState<StreamStatsViewState> _state;
	private readonly string? _accountId;
	private readonly string _metric;
	private readonly bool _samplesHistory;
	private readonly IStreamStatsSource _source;
	private readonly StreamStatsHistoryOpener _history;
	private readonly IStreamThumbnails _thumbnails;
	private readonly StreamWidgetIcon? _icon;
	private readonly ITimer _timer;
	private readonly Lock _sync = new();

	private StreamStatsAccount? _account;
	private IVariableHistoryWindow? _window;
	private int _windowGeneration;
	private bool _disposed;

	public StreamStatsWidgetSession(
		StreamPlatform platform,
		UiView view,
		UiState<StreamStatsViewState> state,
		string? accountId,
		StreamStatsWidgetOptions options,
		IStreamStatsSource source,
		StreamStatsHistoryOpener history,
		IStreamThumbnails thumbnails,
		TimeProvider time,
		StreamWidgetIcon? icon = null)
	{
		ArgumentNullException.ThrowIfNull(platform);
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(source);
		ArgumentNullException.ThrowIfNull(history);
		ArgumentNullException.ThrowIfNull(thumbnails);
		ArgumentNullException.ThrowIfNull(options);
		ArgumentNullException.ThrowIfNull(time);

		_platform = platform;
		_view = view;
		_state = state;
		_accountId = accountId;
		_metric = platform.Stats.NormalizeMetric(options.Metric);
		_samplesHistory = StreamStatsStyles.Normalize(options.Style) == StreamStatsStyles.ValueGraph;
		_source = source;
		_history = history;
		_thumbnails = thumbnails;
		_icon = icon;

		_source.Changed += OnAccountsChanged;
		_thumbnails.Changed += OnThumbnailChanged;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		Refresh(true);
		_timer = time.CreateTimer(_ => OnTick(), null, RefreshInterval, RefreshInterval);
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
		_view.Dispatch(uiEvent);
	}

	public async ValueTask DisposeAsync()
	{
		_source.Changed -= OnAccountsChanged;
		_thumbnails.Changed -= OnThumbnailChanged;

		IVariableHistoryWindow? window;
		lock (_sync)
		{
			_disposed = true;
			window = Detach();
		}

		await _timer.DisposeAsync().ConfigureAwait(false);

		if (window is not null)
		{
			await window.DisposeAsync().ConfigureAwait(false);
		}

		_view.Dispose();
	}

	internal void OnTick()
	{
		_icon?.ResolveIfPending(() => Refresh(false));
		Refresh(false);
	}

	private void OnAccountsChanged(object? sender, EventArgs args)
	{
		Refresh(true);
	}

	private void OnSampled(object? sender, EventArgs args)
	{
		Refresh(false);
	}

	private void OnThumbnailChanged(object? sender, string accountId)
	{
		if (string.Equals(_account?.AccountId, accountId, StringComparison.Ordinal))
		{
			Refresh(false);
		}
	}

	private void Refresh(bool rebind)
	{
		IVariableHistoryWindow? released = null;

		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			if (rebind)
			{
				released = Rebind();
			}

			using (_view.Batch())
			{
				_state.Set(StreamStatsResolver.Resolve(_platform,
					_source,
					_account,
					_window?.Values ?? [],
					_thumbnails,
					_metric));
				_icon?.Apply();
			}
		}

		if (released is not null)
		{
			_ = released.DisposeAsync().AsTask();
		}
	}

	private IVariableHistoryWindow? Rebind()
	{
		var accounts = _source.Accounts;
		var next = _accountId is null
			? accounts.Count > 0 ? accounts[0] : null
			: accounts.FirstOrDefault(a => a.AccountId == _accountId);

		if (next?.VariablePrefix == _account?.VariablePrefix && next?.AccountId == _account?.AccountId)
		{
			_account = next;
			return null;
		}

		var released = Detach();
		_account = next;

		if (next is not null && _samplesHistory && _platform.Stats.Metric(_metric) is { } metric)
		{
			_ = OpenHistoryAsync(StreamStatsResolver.VariableName(next, metric.VariableName), ++_windowGeneration);
		}

		return released;
	}

	private IVariableHistoryWindow? Detach()
	{
		_windowGeneration++;
		var window = _window;
		_window = null;

		if (window is not null)
		{
			window.Changed -= OnSampled;
		}

		return window;
	}

	// Started while the session lock is held, so it yields before touching the session again.
	private async Task OpenHistoryAsync(string variableName, int generation)
	{
		await Task.Yield();

		IVariableHistoryWindow? window;
		try
		{
			window = await _history(variableName, HistoryLength, CancellationToken.None).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is VariableHistoryException or OperationCanceledException)
		{
			return;
		}

		if (window is null)
		{
			return;
		}

		lock (_sync)
		{
			if (!_disposed && generation == _windowGeneration)
			{
				_window = window;
				window.Changed += OnSampled;
				window = null;
			}
		}

		if (window is not null)
		{
			await window.DisposeAsync().ConfigureAwait(false);
			return;
		}

		Refresh(false);
	}
}
