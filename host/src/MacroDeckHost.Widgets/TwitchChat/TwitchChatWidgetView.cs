using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.TwitchChat;

internal static class TwitchChatWidgetView
{
	public const int MessageMaxLines = 4;
	public const int FallbackMaxLines = 2;

	// Capped so a large tile shows more lines instead of larger text.
	private static readonly UiLength _textSize = UiLength.Capped(0.1, 12);

	// At least a fifth of the text size, so the clipped edge never cuts the last line's descenders.
	private static readonly UiLength _logPadding = UiLength.Capped(0.025, 3);

	private static readonly UiLength _lineGap = UiLength.Capped(0.015, 2);

	public static UiElement Build(
		UiState<TwitchChatViewState> state,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius)
	{
		ArgumentNullException.ThrowIfNull(state);

		return new UiStack
		{
			Key = "twitchChat",
			Direction = UiComponentDirections.Vertical,
			Justify = UiComponentJustify.Center,
			Padding = WidgetSafeArea.For(cornerRadius),
			Children =
			[
				new UiWhen
				{
					Key = "offlineGate",
					Condition = () => state.Value.Status == TwitchChatStatus.Offline,
					Content = () => Notice("offline", AppStrings.Integrations.Twitch.ChatWidget.Offline()),
				},
				new UiWhen
				{
					Key = "emptyGate",
					Condition = () => state.Value.Status == TwitchChatStatus.Empty,
					Content = () => Notice("empty", AppStrings.Integrations.Twitch.ChatWidget.Empty()),
				},
				new UiWhen
				{
					Key = "messagesGate",
					Condition = () => state.Value.Status == TwitchChatStatus.Messages,
					Content = () => Log(state),
				},
			],
		};
	}

	public static UiTextRun Message(TwitchChatLine line)
	{
		ArgumentNullException.ThrowIfNull(line);

		return new UiTextRun
		{
			Key = line.Key,
			Text = line.Text,
			Spans = UiValue.Of(line.Spans),
			Size = _textSize,
			Wrap = true,
			MaxLines = MessageMaxLines,
		};
	}

	public static UiTextRun FallbackMessage(TwitchChatLine line)
	{
		ArgumentNullException.ThrowIfNull(line);

		return new UiTextRun
		{
			Key = line.FallbackKey,
			Text = line.Text,
			Size = _textSize,
			Wrap = true,
			MaxLines = FallbackMaxLines,
		};
	}

	private static UiStack Log(UiState<TwitchChatViewState> state)
		=> new()
		{
			Key = "log",
			Direction = UiComponentDirections.Vertical,
			Fill = true,
			Overflow = UiComponentOverflows.ClipStart,
			RequiredComponentVersion = 2,
			Padding = _logPadding,
			Gap = _lineGap,
			Children =
			[
				new UiRepeat<TwitchChatLine>
				{
					Key = "messages",
					Items = UiValue.From(() => state.Value.Lines),
					KeySelector = line => line.Key,
					Template = (line, _) => Message(line),
				},
			],
			Fallback = new UiStack
			{
				Key = "logFallback",
				Direction = UiComponentDirections.Vertical,
				Fill = true,
				Justify = UiComponentJustify.End,
				Padding = _logPadding,
				Gap = _lineGap,
				Children =
				[
					new UiRepeat<TwitchChatLine>
					{
						Key = "fallbackMessages",
						Items = UiValue.From(() => state.Value.FallbackLines),
						KeySelector = line => line.FallbackKey,
						Template = (line, _) => FallbackMessage(line),
					},
				],
			},
		};

	private static UiTextRun Notice(string key, UiText text)
		=> new()
		{
			Key = key,
			Text = text,
			Size = _textSize,
			Role = UiComponentTextRoles.Secondary,
			Align = UiComponentAlignments.Center,
			Wrap = true,
			MaxLines = 3,
		};
}
