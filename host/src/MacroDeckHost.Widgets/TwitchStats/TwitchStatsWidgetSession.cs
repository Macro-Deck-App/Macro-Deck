using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Twitch.Stats;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Widgets.HistoryGraph;

namespace MacroDeckHost.Widgets.TwitchStats;

internal sealed class TwitchStatsWidgetSession : IUiSession
{
	public const int HistoryLength = 40;

	private readonly UiView _view;
	private readonly UiState<TwitchStatsViewState> _state;
	private readonly string? _accountId;
	private readonly string _metric;
	private readonly bool _samplesHistory;
	private readonly ITwitchStatsAccounts _accounts;
	private readonly VariableRegistry _variables;
	private readonly IVariableHistory _history;
	private readonly IVariableChangeNotifier _notifier;
	private readonly ITwitchStreamThumbnails _thumbnails;
	private readonly Lock _sync = new();

	private TwitchStatsAccount? _account;
	private IVariableHistoryWindow _window = EmptyVariableHistoryWindow.Instance;
	private bool _disposed;

	public TwitchStatsWidgetSession(
		UiView view,
		UiState<TwitchStatsViewState> state,
		string? accountId,
		TwitchStatsWidgetOptions options,
		ITwitchStatsAccounts accounts,
		VariableRegistry variables,
		IVariableHistory history,
		IVariableChangeNotifier notifier,
		ITwitchStreamThumbnails thumbnails)
	{
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(accounts);
		ArgumentNullException.ThrowIfNull(variables);
		ArgumentNullException.ThrowIfNull(history);
		ArgumentNullException.ThrowIfNull(notifier);
		ArgumentNullException.ThrowIfNull(thumbnails);
		ArgumentNullException.ThrowIfNull(options);

		_view = view;
		_state = state;
		_accountId = accountId;
		_metric = TwitchStatsMetrics.Normalize(options.Metric);
		_samplesHistory = TwitchStatsStyles.Normalize(options.Style) == TwitchStatsStyles.ValueGraph;
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

	private void OnThumbnailChanged(object? sender, string userId)
	{
		if (string.Equals(_account?.UserId, userId, StringComparison.Ordinal))
		{
			Refresh(rebind: false);
		}
	}

	private void OnVariableChanged(object? sender, VariableChangedEventArgs args)
	{
		if (_account is { } account &&
			args.Name.StartsWith(TwitchStatsResolver.VariableName(account.VariableKey, string.Empty), StringComparison.Ordinal))
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
				_state.Set(TwitchStatsResolver.Resolve(_variables, _account, _window.Values, _thumbnails, _metric));
			}
		}
	}

	private void Rebind()
	{
		var accounts = _accounts.Accounts;
		var next = _accountId is null
			? accounts.Count > 0 ? accounts[0] : null
			: accounts.FirstOrDefault(a => a.UserId == _accountId);

		if (next?.VariableKey == _account?.VariableKey && next?.UserId == _account?.UserId)
		{
			_account = next;
			return;
		}

		_window.Changed -= OnSampled;
		_window.Dispose();
		_account = next;
		_window = next is null || !_samplesHistory
			? EmptyVariableHistoryWindow.Instance
			: _history.Open(TwitchStatsResolver.VariableName(next.VariableKey, TwitchStatsMetrics.VariableName(_metric)),
				HistoryLength);
		_window.Changed += OnSampled;
	}
}
