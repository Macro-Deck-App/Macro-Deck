using MacroDeck.Localization;
using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.TwitchChat;

internal enum TwitchChatDialogStep
{
	Actions,

	ConfirmBan,

	Busy
}

internal enum TwitchChatSelectionNote
{
	None,

	OwnMessage,

	SharedChat
}

internal sealed record TwitchChatSelection(
	string MessageId,
	string ChatterId,
	string ChatterName,
	string Text,
	TwitchChatSelectionNote Note = TwitchChatSelectionNote.None,
	string? SourceChannel = null);

internal sealed record TwitchChatDialogState(
	TwitchChatViewState Chat,
	bool CanModerate,
	TwitchChatSelection? Selection = null,
	TwitchChatDialogStep Step = TwitchChatDialogStep.Actions,
	LocalizedString? Result = null);

internal sealed record TwitchChatDialogActions(
	Action<string> Select,
	Action Deselect,
	Func<CancellationToken, Task> Delete,
	Func<int, CancellationToken, Task> Timeout,
	Action AskBan,
	Func<CancellationToken, Task> Ban,
	Action CancelBan,
	Func<CancellationToken, Task> Unban);

internal static class TwitchChatDialogView
{
	public const int FallbackMessages = 25;

	private static readonly UiSize _textSize = UiSize.FromBasis(0.042);
	private static readonly UiSize _buttonTextSize = UiSize.FromBasis(0.036);
	private static readonly UiSize _rowPadding = UiSize.FromBasis(0.012);
	private static readonly UiSize _gap = UiSize.FromBasis(0.02);
	private static readonly UiSize _buttonRowHeight = UiSize.FromBasis(0.1);

	public static TwitchChatLineLayout Layout { get; } = new(line => Row(line, null),
		line => Row(line, null, fallback: true),
		MaxMessages: 100,
		MaxBytes: 96 * 1024,
		FallbackMessages);

	public static UiElement Build(UiState<TwitchChatDialogState> state, TwitchChatDialogActions actions)
	{
		ArgumentNullException.ThrowIfNull(state);
		ArgumentNullException.ThrowIfNull(actions);

		return new UiStack
		{
			Key = "twitchChatDialog",
			Direction = UiComponentDirections.Vertical,
			Padding = 0.03,
			Gap = _gap,
			Children =
			[
				new UiWhen
				{
					Key = "offlineGate",
					Condition = () => state.Value.Chat.Status == TwitchChatStatus.Offline,
					Content = () => Notice("offline", AppStrings.Integrations.Twitch.ChatWidget.Offline()),
				},
				new UiWhen
				{
					Key = "emptyGate",
					Condition = () => state.Value.Chat.Status == TwitchChatStatus.Empty,
					Content = () => Notice("empty", AppStrings.Integrations.Twitch.ChatWidget.Empty()),
				},
				new UiWhen
				{
					Key = "messagesGate",
					Condition = () => state.Value.Chat.Status == TwitchChatStatus.Messages,
					Content = () => Log(state, actions),
				},
				new UiWhen
				{
					Key = "panelGate",
					Condition = () => state.Value.Selection is not null,
					Content = () => Panel(state, actions),
				},
			],
		};
	}

	private static UiList Log(UiState<TwitchChatDialogState> state, TwitchChatDialogActions actions)
		=> new()
		{
			Key = "log",
			Fill = true,
			Gap = _gap,
			Anchor = UiComponentListAnchors.End,
			RequiredComponentVersion = 3,
			Children =
			[
				new UiRepeat<TwitchChatLine>
				{
					Key = "messages",
					Items = UiValue.From(() => state.Value.Chat.Lines),
					KeySelector = line => line.Key,
					Template = (line, _) => Row(line, state.Value.CanModerate ? actions : null),
				},
			],
			Fallback = new UiList
			{
				Key = "logFallback",
				Fill = true,
				Gap = _gap,
				Children =
				[
					new UiRepeat<TwitchChatLine>
					{
						Key = "fallbackMessages",
						Items = UiValue.From<IReadOnlyList<TwitchChatLine>>(() =>
							[.. state.Value.Chat.FallbackLines.Reverse()]),
						KeySelector = line => line.FallbackKey,
						Template = (line, _) => Row(line, state.Value.CanModerate ? actions : null, fallback: true),
					},
				],
			},
		};

	private static UiStack Row(TwitchChatLine line, TwitchChatDialogActions? actions, bool fallback = false)
		=> new()
		{
			Key = fallback ? line.FallbackKey : line.Key,
			Padding = _rowPadding,
			Events = actions is null ? [] : [UiEventHandler.On(UiComponentEvents.Press, () => actions.Select(line.Key))],
			Children =
			[
				new UiTextRun
				{
					Key = "text",
					Text = line.Text,
					Spans = UiValue.Of(line.Spans),
					Size = _textSize,
					Wrap = true,
				},
			],
		};

	private static UiStack Panel(UiState<TwitchChatDialogState> state, TwitchChatDialogActions actions)
		=> new()
		{
			Key = "panel",
			Direction = UiComponentDirections.Vertical,
			Gap = _gap,
			Children =
			[
				new UiStack
				{
					Key = "panelHeader",
					Direction = UiComponentDirections.Horizontal,
					Align = UiComponentAlignments.Center,
					Gap = _gap,
					Children =
					[
						new UiTextRun
						{
							Key = "selected",
							Text = UiText.From(() => state.Value.Selection?.Text),
							Size = _textSize,
							Wrap = true,
							MaxLines = 2,
							Fill = true,
						},
						Button("close", AppStrings.Integrations.Twitch.ChatDialog.Close(), actions.Deselect),
					],
				},
				new UiWhen
				{
					Key = "noteGate",
					Condition = () => NoteOf(state.Value.Selection) is not null,
					Content = () => Notice("note", UiText.FromLocalized(() => NoteOf(state.Value.Selection)!.Value)),
				},
				new UiWhen
				{
					Key = "actionsGate",
					Condition = () => NoteOf(state.Value.Selection) is null &&
						state.Value.Step is TwitchChatDialogStep.Actions or TwitchChatDialogStep.Busy,
					Content = () => new UiModifier
					{
						Key = "actionsState",
						Disabled = UiValue.From(() => state.Value.Step == TwitchChatDialogStep.Busy),
						Child = new UiStack
						{
							Key = "actions",
							Direction = UiComponentDirections.Vertical,
							Gap = _gap,
							Children =
							[
								ButtonRow("moderate",
									Button("delete", AppStrings.Integrations.Twitch.ChatDialog.DeleteMessage(), actions.Delete),
									Button("unban", AppStrings.Integrations.Twitch.ChatDialog.Unban(), actions.Unban),
									Button("ban", AppStrings.Integrations.Twitch.ChatDialog.Ban(), actions.AskBan)),
								ButtonRow("timeouts",
									Button("timeout1m",
										AppStrings.Integrations.Twitch.ChatDialog.Timeout1Minute(),
										ct => actions.Timeout(60, ct)),
									Button("timeout10m",
										AppStrings.Integrations.Twitch.ChatDialog.Timeout10Minutes(),
										ct => actions.Timeout(600, ct)),
									Button("timeout1h",
										AppStrings.Integrations.Twitch.ChatDialog.Timeout1Hour(),
										ct => actions.Timeout(3600, ct))),
							],
						},
					},
				},
				new UiWhen
				{
					Key = "confirmGate",
					Condition = () => state.Value.Step == TwitchChatDialogStep.ConfirmBan,
					Content = () => new UiStack
					{
						Key = "confirm",
						Direction = UiComponentDirections.Vertical,
						Gap = _gap,
						Children =
						[
							new UiTextRun
							{
								Key = "confirmText",
								Text = UiText.FromLocalized(() => AppStrings.Integrations.Twitch.ChatDialog.ConfirmBan(
									name: state.Value.Selection?.ChatterName ?? string.Empty)),
								Size = _textSize,
								Wrap = true,
							},
							ButtonRow("confirmButtons",
								Button("confirmBan", AppStrings.Integrations.Twitch.ChatDialog.Ban(), actions.Ban),
								Button("cancelBan", AppStrings.Integrations.Twitch.ChatDialog.Cancel(), actions.CancelBan)),
						],
					},
				},
				new UiWhen
				{
					Key = "resultGate",
					Condition = () => state.Value.Result is not null,
					Content = () => Notice("result", UiText.FromLocalized(() => state.Value.Result!.Value)),
				},
			],
		};

	private static LocalizedString? NoteOf(TwitchChatSelection? selection)
		=> selection?.Note switch
		{
			TwitchChatSelectionNote.OwnMessage => AppStrings.Integrations.Twitch.ChatDialog.OwnMessage(),
			TwitchChatSelectionNote.SharedChat => AppStrings.Integrations.Twitch.ChatDialog.SharedChat(
				channel: selection.SourceChannel ?? string.Empty),
			_ => null,
		};

	private static UiStack ButtonRow(string key, params UiElement[] buttons)
		=> new()
		{
			Key = key,
			Direction = UiComponentDirections.Horizontal,
			Gap = _gap,
			MainSize = _buttonRowHeight,
			Children = buttons,
		};

	private static UiButton Button(string key, LocalizedString label, Action press)
		=> Button(key, label, UiEventHandler.On(UiComponentEvents.Press, press));

	private static UiButton Button(string key, LocalizedString label, Func<CancellationToken, Task> press)
		=> Button(key, label, UiEventHandler.OnAsync(UiComponentEvents.Press, press));

	private static UiButton Button(string key, LocalizedString label, UiEventHandler handler)
		=> new()
		{
			Key = key,
			Fill = true,
			Justify = UiComponentJustify.Center,
			Align = UiComponentAlignments.Center,
			Events = [handler],
			Children =
			[
				new UiTextRun
				{
					Key = "label",
					Text = label,
					Size = _buttonTextSize,
					Weight = UiComponentTextWeights.Medium,
					Align = UiComponentAlignments.Center,
					MaxLines = 1,
				},
			],
		};

	private static UiTextRun Notice(string key, UiText text)
		=> new()
		{
			Key = key,
			Text = text,
			Size = _textSize,
			Role = UiComponentTextRoles.Secondary,
			Wrap = true,
			MaxLines = 3,
		};
}
