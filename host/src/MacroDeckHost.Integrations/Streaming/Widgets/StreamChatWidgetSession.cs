using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;

namespace MacroDeckHost.Integrations.Streaming.Widgets;

internal sealed class StreamChatWidgetSession : IUiSession
{
	private readonly UiView _view;
	private readonly UiState<StreamChatViewState> _state;
	private readonly StreamChatLines _lines;
	private readonly IStreamChatFeed _feed;
	private readonly string? _accountId;
	private readonly StreamWidgetIcon? _icon;
	private readonly Lock _sync = new();

	private string? _shownAccountId;
	private string? _shownAccountLabel;
	private IUiEventOrigin? _pendingOrigin;
	private bool _disposed;

	public StreamChatWidgetSession(
		UiView view,
		UiState<StreamChatViewState> state,
		StreamChatLines lines,
		IStreamChatFeed feed,
		string? accountId,
		string? shownAccountId,
		StreamWidgetIcon? icon = null)
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
		_icon = icon;

		_feed.Changed += OnChanged;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		// A tick landing between the provider's first snapshot and the subscription above is not lost.
		OnChanged(null, new ChatChangedEventArgs(null));
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

	public StreamChatDialogRequest? DialogRequest()
	{
		lock (_sync)
		{
			if (_disposed || _pendingOrigin is not { ClientId.Length: > 0 } origin)
			{
				return null;
			}

			return new StreamChatDialogRequest(origin, _shownAccountId ?? _accountId, _shownAccountLabel);
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

	// Held across the view's dispatch so handlers and chat updates take this lock and the view's in one order.
	private void Deliver(UiEvent uiEvent, IUiEventOrigin? origin)
	{
		lock (_sync)
		{
			_pendingOrigin = origin;
			try
			{
				_view.Dispatch(uiEvent);
			}
			finally
			{
				_pendingOrigin = null;
			}
		}
	}

	private void OnChanged(object? sender, ChatChangedEventArgs args)
	{
		_icon?.ResolveIfPending(() => Refresh(null));
		Refresh(args.AccountId);
	}

	private void Refresh(string? changedAccountId)
	{
		lock (_sync)
		{
			var elsewhere = changedAccountId is not null
				&& !string.Equals(changedAccountId, _shownAccountId, StringComparison.Ordinal);

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
				_icon?.Apply();
			}
		}
	}
}

internal sealed record StreamChatDialogRequest(IUiEventOrigin Origin, string? AccountId, string? AccountLabel);

internal sealed class StreamWidgetIcon
{
	private readonly Func<CancellationToken, Task<UiResource?>> _resolve;
	private readonly Lock _sync = new();
	private UiResource? _resolved;
	private int _pending;

	public StreamWidgetIcon(UiResource? icon, Func<CancellationToken, Task<UiResource?>> resolve)
	{
		_resolve = resolve;
		_resolved = icon;
		_pending = icon is null ? 1 : 0;
		State = new UiState<UiResource?>(icon);
	}

	public UiState<UiResource?> State { get; }

	public void ResolveIfPending(Action refreshed)
	{
		if (Interlocked.Exchange(ref _pending, 0) == 1)
		{
			_ = ResolveAsync(refreshed);
		}
	}

	// Called inside the session's view batch, so the icon lands in the same patch as the content.
	public void Apply()
	{
		UiResource? resolved;
		lock (_sync)
		{
			resolved = _resolved;
		}

		if (!Equals(State.Value, resolved))
		{
			State.Set(resolved);
		}
	}

	private async Task ResolveAsync(Action refreshed)
	{
		var icon = await _resolve(CancellationToken.None).ConfigureAwait(false);

		if (icon is null)
		{
			Volatile.Write(ref _pending, 1);
			return;
		}

		lock (_sync)
		{
			_resolved = icon;
		}

		refreshed();
	}
}
