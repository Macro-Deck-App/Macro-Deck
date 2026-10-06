using MacroDeck.Localization;
using MacroDeck.Sdk.Ui;
using MacroDeck.Ui.Model.Events;
using MacroDeck.Ui.Model.Nodes;
using MacroDeck.Ui.Model.Patches;
using MacroDeck.Ui.Model.Surfaces;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Localization;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Widgets.StreamChat;

internal sealed class StreamChatDialogSession : IUiSession
{
	private readonly StreamPlatform _platform;
	private readonly UiView _view;
	private readonly UiState<StreamChatDialogState> _state;
	private readonly StreamChatLines _lines;
	private readonly IStreamChatFeed _feed;
	private readonly string? _accountId;
	private readonly Func<StreamChatWidgetPermission> _permission;
	private readonly Func<IStreamChatModerator?> _moderator;
	private readonly IHostLockState _lock;
	private readonly ILogger _logger;
	private readonly Lock _sync = new();

	private Dictionary<string, ChatMessage> _messages = new(StringComparer.Ordinal);
	private bool _allowed;
	private bool _disposed;

	public StreamChatDialogSession(
		StreamPlatform platform,
		UiSurface surface,
		StreamChatLines lines,
		IStreamChatFeed feed,
		string? accountId,
		Func<StreamChatWidgetPermission> permission,
		Func<IStreamChatModerator?> moderator,
		IHostLockState hostLock,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(platform);
		ArgumentNullException.ThrowIfNull(surface);

		_platform = platform;
		_lines = lines;
		_feed = feed;
		_accountId = accountId;
		_permission = permission;
		_moderator = moderator;
		_lock = hostLock;
		_logger = logger;

		_state = new UiState<StreamChatDialogState>(new StreamChatDialogState(StreamChatViewState.Offline, false));
		_view = new UiView(surface, StreamChatDialogView.Build(platform, _state, new StreamChatDialogActions(Select,
			Deselect,
			ct => RunAsync(message => ChatModerationRequest.Delete(message.MessageId),
				_ => AppStrings.Integrations.StreamChat.Dialog.Deleted(), ct),
			(seconds, ct) => RunAsync(message => ChatModerationRequest.Timeout(message.AuthorId, seconds),
				name => AppStrings.Integrations.StreamChat.Dialog.TimedOut(name: name), ct),
			AskBan,
			ct => RunAsync(message => ChatModerationRequest.Ban(message.AuthorId),
				name => AppStrings.Integrations.StreamChat.Dialog.Banned(name: name), ct),
			CancelBan,
			ct => RunAsync(message => ChatModerationRequest.Unban(message.AuthorId),
				name => AppStrings.Integrations.StreamChat.Dialog.Unbanned(name: name), ct))));

		_feed.Changed += OnChanged;
		_view.Changed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
		_view.HandlerFaulted += (_, fault)
			=> Faulted?.Invoke(this, new UiSessionFaultedEventArgs(fault.Exception.Message, fault.Exception));

		_allowed = _permission() == StreamChatWidgetPermission.Allowed;
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

	private void OnChanged(object? sender, ChatChangedEventArgs args)
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

			var snapshot = _accountId is null ? ChatSnapshot.None : _feed.Snapshot(_accountId);
			var ownChannel = snapshot.Account is { } account &&
				string.Equals(account.AccountId, _accountId, StringComparison.Ordinal);
			var chat = ownChannel ? _lines.Build(snapshot) : StreamChatViewState.Offline;

			_messages = ownChannel
				? snapshot.Messages.ToDictionary(message => ChatStyle.MessageKey(message.MessageId), StringComparer.Ordinal)
				: new Dictionary<string, ChatMessage>(StringComparer.Ordinal);

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
			if (!_messages.TryGetValue(key, out var message) || !_state.Value.CanModerate ||
				_state.Value.Step == StreamChatDialogStep.Busy)
			{
				return;
			}

			if (_permission() != StreamChatWidgetPermission.Allowed)
			{
				_allowed = false;
				Update(state => state with { CanModerate = false, Selection = null });
				return;
			}

			var note = StreamChatSelectionNote.None;

			if (string.Equals(message.AuthorId, _accountId, StringComparison.Ordinal))
			{
				note = StreamChatSelectionNote.OwnMessage;
			}
			else if (message.SourceChannelId is { } source && !string.Equals(source, _accountId, StringComparison.Ordinal))
			{
				note = StreamChatSelectionNote.SharedChat;
			}

			var line = _state.Value.Chat.Lines.FirstOrDefault(candidate => candidate.Key == key);

			Update(state => state with
			{
				Selection = new StreamChatSelection(message.MessageId,
					message.AuthorId,
					message.AuthorName,
					line?.Text ?? message.AuthorName,
					note,
					message.SourceChannelName ?? message.SourceChannelId),
				Step = StreamChatDialogStep.Actions,
				Result = null,
			});
		}
	}

	private void Deselect()
		=> UpdateLocked(state => state with
		{
			Selection = null,
			Step = state.Step == StreamChatDialogStep.Busy ? StreamChatDialogStep.Busy : StreamChatDialogStep.Actions,
			Result = null,
		});

	private void AskBan()
		=> UpdateLocked(state => state.Step == StreamChatDialogStep.Actions
			? state with { Step = StreamChatDialogStep.ConfirmBan, Result = null }
			: state);

	private void CancelBan()
		=> UpdateLocked(state => state.Step == StreamChatDialogStep.ConfirmBan
			? state with { Step = StreamChatDialogStep.Actions }
			: state);

	private async Task RunAsync(
		Func<StreamChatSelection, ChatModerationRequest> request,
		Func<string, LocalizedString> succeeded,
		CancellationToken cancellationToken)
	{
		StreamChatSelection selection;

		lock (_sync)
		{
			if (_disposed || _state.Value is not { Selection: { Note: StreamChatSelectionNote.None } current } ||
				_state.Value.Step == StreamChatDialogStep.Busy)
			{
				return;
			}

			selection = current;

			if (Refusal() is { } refusal)
			{
				Update(state => state with
				{
					Step = StreamChatDialogStep.Actions,
					Result = refusal,
					CanModerate = state.CanModerate && _allowed,
				});
				return;
			}

			Update(state => state with { Step = StreamChatDialogStep.Busy, Result = null });
		}

		var result = ChatModerationResult.AccountUnavailable;

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
			_logger.Warning(exception, "{Platform} chat moderation from the chat dialog failed", _platform.OwnerId);
			result = ChatModerationResult.Failed;
		}

		var text = result == ChatModerationResult.Succeeded
			? succeeded(selection.AuthorName)
			: FailureText(result);

		UpdateLocked(state => state.Selection?.MessageId == selection.MessageId
			? state with { Step = StreamChatDialogStep.Actions, Result = text }
			: state with { Step = StreamChatDialogStep.Actions });
	}

	private LocalizedString? Refusal()
	{
		_allowed = _permission() == StreamChatWidgetPermission.Allowed;

		if (_lock.IsLocked)
		{
			return AppStrings.Errors.Common.HostLocked();
		}

		return _permission() switch
		{
			StreamChatWidgetPermission.Allowed => null,
			StreamChatWidgetPermission.AccountChanged => _platform.Chat.AccountChanged(),
			_ => AppStrings.Integrations.StreamChat.Dialog.ModerationUnavailable(),
		};
	}

	private LocalizedString FailureText(ChatModerationResult result)
		=> result switch
		{
			ChatModerationResult.AccountUnavailable => _platform.Chat.AccountUnavailable(),
			ChatModerationResult.MissingScope => _platform.Chat.MissingScope(),
			ChatModerationResult.NotPermitted => _platform.Chat.NotPermitted(),
			ChatModerationResult.Refused => _platform.Chat.Refused(),
			ChatModerationResult.Unsupported when _platform.Chat.Unsupported is { } unsupported => unsupported(),
			_ => AppStrings.Integrations.StreamChat.Dialog.Failed(),
		};

	private void UpdateLocked(Func<StreamChatDialogState, StreamChatDialogState> change)
	{
		lock (_sync)
		{
			if (!_disposed)
			{
				Update(change);
			}
		}
	}

	private void Update(Func<StreamChatDialogState, StreamChatDialogState> change)
	{
		using (_view.Batch())
		{
			_state.Set(change(_state.Value));
		}
	}
}

internal enum StreamChatWidgetPermission
{
	Allowed,

	TurnedOff,

	AccountChanged
}
