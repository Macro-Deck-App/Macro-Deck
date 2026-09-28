using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Localization;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.TwitchChat;

internal sealed class TwitchChatDialogSession : IUiSession
{
	private readonly UiView _view;
	private readonly UiState<TwitchChatDialogState> _state;
	private readonly TwitchChatLines _lines;
	private readonly ITwitchChatFeed _feed;
	private readonly string? _accountId;
	private readonly Func<TwitchChatWidgetPermission> _permission;
	private readonly Func<ITwitchChatModerator?> _moderator;
	private readonly IHostLockState _lock;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();

	private Dictionary<string, TwitchChatMessage> _messages = new(StringComparer.Ordinal);
	private bool _allowed;
	private bool _disposed;

	public TwitchChatDialogSession(
		UiSurface surface,
		TwitchChatLines lines,
		ITwitchChatFeed feed,
		string? accountId,
		Func<TwitchChatWidgetPermission> permission,
		Func<ITwitchChatModerator?> moderator,
		IHostLockState hostLock,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(surface);

		_lines = lines;
		_feed = feed;
		_accountId = accountId;
		_permission = permission;
		_moderator = moderator;
		_lock = hostLock;
		_logger = logger;

		_state = new UiState<TwitchChatDialogState>(new TwitchChatDialogState(TwitchChatViewState.Offline, false));
		_view = new UiView(surface, TwitchChatDialogView.Build(_state, new TwitchChatDialogActions(Select,
			Deselect,
			ct => RunAsync(message => TwitchChatModerationRequest.Delete(message.MessageId),
				_ => AppStrings.Integrations.Twitch.ChatDialog.Deleted(), ct),
			(seconds, ct) => RunAsync(message => TwitchChatModerationRequest.Timeout(message.ChatterId, seconds),
				name => AppStrings.Integrations.Twitch.ChatDialog.TimedOut(name: name), ct),
			AskBan,
			ct => RunAsync(message => TwitchChatModerationRequest.Ban(message.ChatterId),
				name => AppStrings.Integrations.Twitch.ChatDialog.Banned(name: name), ct),
			CancelBan,
			ct => RunAsync(message => TwitchChatModerationRequest.Unban(message.ChatterId),
				name => AppStrings.Integrations.Twitch.ChatDialog.Unbanned(name: name), ct))));

		_feed.Changed += OnChanged;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_allowed = _permission() == TwitchChatWidgetPermission.Allowed;
		Refresh();
	}

	public event EventHandler? Changed;

	public event EventHandler<UiSessionFaultedEventArgs>? Faulted;

	public UiTree BuildTree() => _view.Tree;

	public IReadOnlyList<UiPatch> DrainPatches() => _view.DrainPatches();

	// Held across the view's dispatch so handlers and chat updates take this lock and the view's in one order.
	public void Dispatch(UiEvent uiEvent)
	{
		lock (_sync)
		{
			_view.Dispatch(uiEvent);
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

	private void OnChanged(object? sender, TwitchChatChangedEventArgs args)
	{
		if (args.AccountId is null || string.Equals(args.AccountId, _accountId, StringComparison.Ordinal))
		{
			Refresh();
		}
	}

	private void Refresh()
	{
		lock (_sync)
		{
			if (_disposed)
			{
				return;
			}

			var snapshot = _accountId is null ? TwitchChatSnapshot.None : _feed.Snapshot(_accountId);
			var ownChannel = snapshot.Account is { } account &&
				string.Equals(account.UserId, _accountId, StringComparison.Ordinal);
			var chat = ownChannel ? _lines.Build(snapshot) : TwitchChatViewState.Offline;

			_messages = ownChannel
				? snapshot.Messages.ToDictionary(message => TwitchChatStyle.MessageKey(message.MessageId), StringComparer.Ordinal)
				: new Dictionary<string, TwitchChatMessage>(StringComparer.Ordinal);

			var canModerate = ownChannel && snapshot.IsConnected && _allowed;

			Update(state => state with
			{
				Chat = chat,
				CanModerate = canModerate,
				Selection = canModerate ? state.Selection : null,
			});
		}
	}

	private void Select(string key)
	{
		lock (_sync)
		{
			if (!_messages.TryGetValue(key, out var message) || !_state.Value.CanModerate)
			{
				return;
			}

			if (_permission() != TwitchChatWidgetPermission.Allowed)
			{
				_allowed = false;
				Update(state => state with { CanModerate = false, Selection = null });
				return;
			}

			var note = TwitchChatSelectionNote.None;

			if (string.Equals(message.ChatterId, _accountId, StringComparison.Ordinal))
			{
				note = TwitchChatSelectionNote.OwnMessage;
			}
			else if (message.SourceChannelId is { } source && !string.Equals(source, _accountId, StringComparison.Ordinal))
			{
				note = TwitchChatSelectionNote.SharedChat;
			}

			var line = _state.Value.Chat.Lines.FirstOrDefault(candidate => candidate.Key == key);

			Update(state => state with
			{
				Selection = new TwitchChatSelection(message.MessageId,
					message.ChatterId,
					message.ChatterName,
					line?.Text ?? message.ChatterName,
					note,
					message.SourceChannelName ?? message.SourceChannelId),
				Step = TwitchChatDialogStep.Actions,
				Result = null,
			});
		}
	}

	private void Deselect()
		=> UpdateLocked(state => state.Step == TwitchChatDialogStep.Busy
			? state
			: state with { Selection = null, Step = TwitchChatDialogStep.Actions, Result = null });

	private void AskBan()
		=> UpdateLocked(state => state.Step == TwitchChatDialogStep.Actions
			? state with { Step = TwitchChatDialogStep.ConfirmBan, Result = null }
			: state);

	private void CancelBan()
		=> UpdateLocked(state => state.Step == TwitchChatDialogStep.ConfirmBan
			? state with { Step = TwitchChatDialogStep.Actions }
			: state);

	private async Task RunAsync(
		Func<TwitchChatSelection, TwitchChatModerationRequest> request,
		Func<string, LocalizedString> succeeded,
		CancellationToken cancellationToken)
	{
		TwitchChatSelection selection;

		lock (_sync)
		{
			if (_disposed || _state.Value is not { Selection: { Note: TwitchChatSelectionNote.None } current } ||
				_state.Value.Step == TwitchChatDialogStep.Busy)
			{
				return;
			}

			selection = current;

			if (Refusal() is { } refusal)
			{
				Update(state => state with { Step = TwitchChatDialogStep.Actions, Result = refusal });
				return;
			}

			Update(state => state with { Step = TwitchChatDialogStep.Busy, Result = null });
		}

		var result = TwitchChatModerationResult.AccountUnavailable;

		try
		{
			if (_moderator() is { } moderator)
			{
				result = await moderator.ModerateAsync(_accountId!, request(selection), cancellationToken)
					.ConfigureAwait(false);
			}
		}
		catch (Exception exception) when (exception is not OperationCanceledException ||
			!cancellationToken.IsCancellationRequested)
		{
			_logger.Warning(exception, "Twitch chat moderation from the chat dialog failed");
			result = TwitchChatModerationResult.Failed;
		}

		var text = result == TwitchChatModerationResult.Succeeded
			? succeeded(selection.ChatterName)
			: FailureText(result);

		UpdateLocked(state => state with { Step = TwitchChatDialogStep.Actions, Result = text });
	}

	private LocalizedString? Refusal()
	{
		_allowed = _permission() == TwitchChatWidgetPermission.Allowed;

		if (_lock.IsLocked)
		{
			return AppStrings.Errors.Common.HostLocked();
		}

		return _permission() switch
		{
			TwitchChatWidgetPermission.Allowed => null,
			TwitchChatWidgetPermission.AccountChanged => AppStrings.Integrations.Twitch.ChatDialog.AccountUnavailable(),
			_ => AppStrings.Integrations.Twitch.ChatDialog.ModerationUnavailable(),
		};
	}

	private static LocalizedString FailureText(TwitchChatModerationResult result)
		=> result switch
		{
			TwitchChatModerationResult.AccountUnavailable => AppStrings.Integrations.Twitch.ChatDialog.AccountUnavailable(),
			TwitchChatModerationResult.MissingScope => AppStrings.Integrations.Twitch.Errors.MissingScopePermission(),
			TwitchChatModerationResult.NotPermitted => AppStrings.Integrations.Twitch.ChatDialog.NotPermitted(),
			TwitchChatModerationResult.Refused => AppStrings.Integrations.Twitch.ChatDialog.Refused(),
			_ => AppStrings.Integrations.Twitch.Errors.RequestRejected(),
		};

	private void UpdateLocked(Func<TwitchChatDialogState, TwitchChatDialogState> change)
	{
		lock (_sync)
		{
			if (!_disposed)
			{
				Update(change);
			}
		}
	}

	private void Update(Func<TwitchChatDialogState, TwitchChatDialogState> change)
	{
		using (_view.Batch())
		{
			_state.Set(change(_state.Value));
		}
	}
}

internal enum TwitchChatWidgetPermission
{
	Allowed,

	TurnedOff,

	AccountChanged
}
