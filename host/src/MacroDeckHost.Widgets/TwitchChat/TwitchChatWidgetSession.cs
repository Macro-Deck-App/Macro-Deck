using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Twitch.Chat;

namespace MacroDeckHost.Widgets.TwitchChat;

internal sealed class TwitchChatWidgetSession : IUiSession
{
	private readonly UiView _view;
	private readonly UiState<TwitchChatViewState> _state;
	private readonly TwitchChatLines _lines;
	private readonly ITwitchChatFeed _feed;
	private readonly string? _accountId;
	private readonly Lock _sync = new();

	private string? _shownAccountId;
	private bool _disposed;

	public TwitchChatWidgetSession(
		UiView view,
		UiState<TwitchChatViewState> state,
		TwitchChatLines lines,
		ITwitchChatFeed feed,
		string? accountId,
		string? shownAccountId)
	{
		ArgumentNullException.ThrowIfNull(view);
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(lines);
		ArgumentNullException.ThrowIfNull(feed);

		_view = view;
		_state = state;
		_lines = lines;
		_feed = feed;
		_accountId = accountId;
		_shownAccountId = shownAccountId;

		_feed.Changed += OnChanged;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		// A tick landing between the provider's first snapshot and the subscription above is not lost.
		OnChanged(null, new TwitchChatChangedEventArgs(null));
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => _view.Dispatch(uiEvent);

	public ValueTask DisposeAsync()
	{
		_feed.Changed -= OnChanged;

		lock (_sync)
		{
			_disposed = true;
		}

		_view.Dispose();

		return ValueTask.CompletedTask;
	}

	private void OnChanged(object? sender, TwitchChatChangedEventArgs args)
	{
		lock (_sync)
		{
			var elsewhere = args.AccountId is not null &&
				!string.Equals(args.AccountId, _shownAccountId, StringComparison.Ordinal);

			if (_disposed || elsewhere)
			{
				return;
			}

			var snapshot = _feed.Snapshot(_accountId);
			_shownAccountId = snapshot.Account?.UserId;

			// One patch per hub tick, however many lines arrived or left in it.
			using (_view.Batch())
			{
				_state.Set(_lines.Build(snapshot));
			}
		}
	}
}
