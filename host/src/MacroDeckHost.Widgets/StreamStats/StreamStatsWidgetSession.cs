using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.StreamStats;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Widgets.HistoryGraph;

namespace MacroDeckHost.Widgets.StreamStats;

internal sealed class StreamStatsWidgetSession : IUiSession
{
	public const int HistoryLength = 40;

	private readonly StreamPlatform _platform;
	private readonly UiView _view;
	private readonly UiState<StreamStatsViewState> _state;
	private readonly string? _accountId;
	private readonly string _metric;
	private readonly bool _samplesHistory;
	private readonly IStreamStatsAccounts _accounts;
	private readonly VariableRegistry _variables;
	private readonly IVariableHistory _history;
	private readonly IVariableChangeNotifier _notifier;
	private readonly IStreamThumbnails _thumbnails;
	private readonly Lock _sync = new();

	private StreamStatsAccount? _account;
	private IVariableHistoryWindow _window = EmptyVariableHistoryWindow.Instance;
	private bool _disposed;

	public StreamStatsWidgetSession(
		StreamPlatform platform,
		UiView view,
		UiState<StreamStatsViewState> state,
		string? accountId,
		StreamStatsWidgetOptions options,
		IStreamStatsAccounts accounts,
		VariableRegistry variables,
		IVariableHistory history,
		IVariableChangeNotifier notifier,
		IStreamThumbnails thumbnails)
	{
		ArgumentNullException.ThrowIfNull(platform);
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(accounts);
		ArgumentNullException.ThrowIfNull(variables);
		ArgumentNullException.ThrowIfNull(history);
		ArgumentNullException.ThrowIfNull(notifier);
		ArgumentNullException.ThrowIfNull(thumbnails);
		ArgumentNullException.ThrowIfNull(options);

		_platform = platform;
		_view = view;
		_state = state;
		_accountId = accountId;
		_metric = platform.Stats.NormalizeMetric(options.Metric);
		_samplesHistory = StreamStatsStyles.Normalize(options.Style) == StreamStatsStyles.ValueGraph;
		_accounts = accounts;
		_variables = variables;
		_history = history;
		_notifier = notifier;
		_thumbnails = thumbnails;

		_accounts.Changed += OnAccountsChanged;
		_notifier.Changed += OnVariableChanged;
		_thumbnails.Changed += OnThumbnailChanged;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		Refresh(rebind: true);
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync()
	{
		_accounts.Changed -= OnAccountsChanged;
		_notifier.Changed -= OnVariableChanged;
		_thumbnails.Changed -= OnThumbnailChanged;

		lock (_sync)
		{
			_disposed = true;
			_window.Changed -= OnSampled;
			_window.Dispose();
			_window = EmptyVariableHistoryWindow.Instance;
		}

		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private void OnAccountsChanged(object? sender, EventArgs args) => Refresh(rebind: true);

	private void OnSampled(object? sender, EventArgs args) => Refresh(rebind: false);

	private void OnThumbnailChanged(object? sender, string accountId)
	{
		if (string.Equals(_account?.AccountId, accountId, StringComparison.Ordinal))
		{
			Refresh(rebind: false);
		}
	}

	private void OnVariableChanged(object? sender, VariableChangedEventArgs args)
	{
		if (_account is { } account &&
			args.Name.StartsWith(account.VariablePrefix, StringComparison.Ordinal))
		{
			Refresh(rebind: false);
		}
	}

	private void Refresh(bool rebind)
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			if (rebind)
			{
				Rebind();
			}

			using (_view.Batch())
			{
				_state.Set(StreamStatsResolver.Resolve(_platform, _variables, _account, _window.Values, _thumbnails,
					_metric));
			}
		}
	}

	private void Rebind()
	{
		var accounts = _accounts.Accounts;
		var next = _accountId is null
			? accounts.Count > 0 ? accounts[0] : null
			: accounts.FirstOrDefault(a => a.AccountId == _accountId);

		if (next?.VariablePrefix == _account?.VariablePrefix && next?.AccountId == _account?.AccountId)
		{
			_account = next;
			return;
		}

		_window.Changed -= OnSampled;
		_window.Dispose();
		_account = next;
		_window = next is null || !_samplesHistory || _platform.Stats.Metric(_metric) is not { } metric
			? EmptyVariableHistoryWindow.Instance
			: _history.Open(StreamStatsResolver.VariableName(next, metric.VariableName), HistoryLength);
		_window.Changed += OnSampled;
	}
}
