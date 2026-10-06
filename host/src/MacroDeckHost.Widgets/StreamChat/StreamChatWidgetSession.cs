using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Application.Ui.Sessions.InProcess;

namespace MacroDeckHost.Widgets.StreamChat;

internal sealed class StreamChatWidgetSession : IUiSession, IOriginAwareUiSession
{
	private readonly UiView _view;
	private readonly UiState<StreamChatViewState> _state;
	private readonly StreamChatLines _lines;
	private readonly IStreamChatFeed _feed;
	private readonly string? _accountId;
	private readonly Lock _sync = new();

	private string? _shownAccountId;
	private string? _shownAccountLabel;
	private string? _pendingOriginClientId;
	private bool _disposed;

	public StreamChatWidgetSession(
		UiView view,
		UiState<StreamChatViewState> state,
		StreamChatLines lines,
		IStreamChatFeed feed,
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
		OnChanged(null, new ChatChangedEventArgs(null));
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	public void Dispatch(UiEvent uiEvent) => Dispatch(uiEvent, null);

	// Held across the view's dispatch so handlers and chat updates take this lock and the view's in one order.
	public void Dispatch(UiEvent uiEvent, string? originClientId)
	{
		lock (_sync)
		{
			_pendingOriginClientId = originClientId;
			_view.Dispatch(uiEvent);
		}
	}

	public StreamChatDialogRequest? DialogRequest()
	{
		lock (_sync)
		{
			if (_disposed || string.IsNullOrEmpty(_pendingOriginClientId))
			{
				return null;
			}

			return new StreamChatDialogRequest(_pendingOriginClientId,
				_shownAccountId ?? _accountId,
				_shownAccountLabel);
		}
	}

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

	private void OnChanged(object? sender, ChatChangedEventArgs args)
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
			_shownAccountId = snapshot.Account?.AccountId;
			_shownAccountLabel = snapshot.Account?.Label;

			using (_view.Batch())
			{
				_state.Set(_lines.Build(snapshot));
			}
		}
	}
}

internal sealed record StreamChatDialogRequest(string OriginClientId, string? AccountId, string? AccountLabel);
