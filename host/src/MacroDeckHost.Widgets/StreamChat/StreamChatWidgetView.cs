using MacroDeck.Ui.Components;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Model.Resources;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Widgets.StreamChat;

internal static class StreamChatWidgetView
{
	public const int FallbackMaxLines = 2;

	private sealed record Metrics(
		UiLength TextSize,
		UiLength LogPadding,
		UiLength LineGap,
		UiLength HeaderGap,
		string? MessageColor = null)
	{
		// The padding stays at least a fifth of the text size, so the clipped edge
		// never cuts the last line's descenders.
		public static Metrics For(double scale, string? messageColor = null) => new(
			UiLength.Capped(0.1 * scale, 12 * scale),
			UiLength.Capped(0.025 * scale, 3 * scale),
			UiLength.Capped(0.015 * scale, 2 * scale),
			UiLength.Capped(0.03 * scale, 4 * scale),
			messageColor);
	}

	public static UiElement Build(
		StreamPlatform platform,
		UiState<StreamChatViewState> state,
		int cornerRadius = WidgetSafeArea.DefaultCornerRadius,
		UiResource? icon = null,
		IReadOnlyList<UiEventHandler>? events = null,
		string? backgroundColor = null,
		double scale = 1,
		string? messageColor = null)
	{
		ArgumentNullException.ThrowIfNull(platform);
		ArgumentNullException.ThrowIfNull(state);

		var metrics = Metrics.For(scale, messageColor);

		return new UiStack
		{
			Key = "twitchChat",
			Events = events ?? [],
			Direction = UiComponentDirections.Vertical,
			Padding = WidgetSafeArea.For(cornerRadius),
			Background = backgroundColor is { } background ? UiValue.Of(background) : UiValue.None<string>(),
			Gap = metrics.HeaderGap,
			Children =
			[
				Header(platform, state, icon, metrics),
				new UiStack
				{
					Key = "body",
					Direction = UiComponentDirections.Vertical,
					Justify = UiComponentJustify.Center,
					Fill = true,
					Children =
					[
						new UiWhen
						{
							Key = "offlineGate",
							Condition = () => state.Value.Status == StreamChatStatus.Offline,
							Content = () => Notice("offline", platform.Chat.Offline(), metrics),
						},
						new UiWhen
						{
							Key = "emptyGate",
							Condition = () => state.Value.Status == StreamChatStatus.Empty,
							Content = () => Notice("empty", AppStrings.Integrations.StreamChat.Widget.Empty(), metrics),
						},
						new UiWhen
						{
							Key = "messagesGate",
							Condition = () => state.Value.Status == StreamChatStatus.Messages,
							Content = () => Log(state, metrics),
						},
					],
				},
			],
		};
	}

	private static UiStack Header(StreamPlatform platform, UiState<StreamChatViewState> state, UiResource? icon,
		Metrics metrics)
	{
		var title = new UiTextRun
		{
			Key = "title",
			Text = UiText.FromLocalized(() => state.Value.AccountName is { } account
				? platform.Chat.Title(account)
				: platform.Chat.Name()),
			Size = metrics.TextSize,
			Weight = UiComponentTextWeights.SemiBold,
			Fill = true,
		};

		return new UiStack
		{
			Key = "header",
			Direction = UiComponentDirections.Horizontal,
			Align = UiComponentAlignments.Center,
			Gap = metrics.HeaderGap,
			Padding = metrics.LogPadding,
			Children = icon is null
				? [title]
				: [new UiImage { Key = "twitchIcon", Source = icon, Size = metrics.TextSize }, title],
		};
	}

	public static UiTextRun Message(StreamChatLine line, double scale = 1, string? messageColor = null)
		=> Message(line, Metrics.For(scale, messageColor));

	public static UiTextRun FallbackMessage(StreamChatLine line, double scale = 1, string? messageColor = null)
		=> FallbackMessage(line, Metrics.For(scale, messageColor));

	private static UiTextRun Message(StreamChatLine line, Metrics metrics)
	{
		ArgumentNullException.ThrowIfNull(line);

		return new UiTextRun
		{
			Key = line.Key,
			Text = line.Text,
			Spans = UiValue.Of(line.Spans),
			Size = metrics.TextSize,
			Color = Tint(metrics.MessageColor),
			Wrap = true,
		};
	}

	private static UiTextRun FallbackMessage(StreamChatLine line, Metrics metrics)
	{
		ArgumentNullException.ThrowIfNull(line);

		return new UiTextRun
		{
			Key = line.FallbackKey,
			Text = line.Text,
			Size = metrics.TextSize,
			Color = Tint(metrics.MessageColor),
			Wrap = true,
			MaxLines = FallbackMaxLines,
		};
	}

	private static UiStack Log(UiState<StreamChatViewState> state, Metrics metrics)
		=> new()
		{
			Key = "log",
			Direction = UiComponentDirections.Vertical,
			Fill = true,
			Overflow = UiComponentOverflows.ClipStart,
			RequiredComponentVersion = 2,
			Padding = metrics.LogPadding,
			Gap = metrics.LineGap,
			Children =
			[
				new UiRepeat<StreamChatLine>
				{
					Key = "messages",
					Items = UiValue.From(() => state.Value.Lines),
					KeySelector = line => line.Key,
					Template = (line, _) => Message(line, metrics),
				},
			],
			Fallback = new UiStack
			{
				Key = "logFallback",
				Direction = UiComponentDirections.Vertical,
				Fill = true,
				Justify = UiComponentJustify.End,
				Padding = metrics.LogPadding,
				Gap = metrics.LineGap,
				Children =
				[
					new UiRepeat<StreamChatLine>
					{
						Key = "fallbackMessages",
						Items = UiValue.From(() => state.Value.FallbackLines),
						KeySelector = line => line.FallbackKey,
						Template = (line, _) => FallbackMessage(line, metrics),
					},
				],
			},
		};

	private static UiValue<string> Tint(string? color)
		=> color is null ? UiValue.None<string>() : UiValue.Of(color);

	private static UiTextRun Notice(string key, UiText text, Metrics metrics)
		=> new()
		{
			Key = key,
			Text = text,
			Size = metrics.TextSize,
			Role = UiComponentTextRoles.Secondary,
			Align = UiComponentAlignments.Center,
			Wrap = true,
			MaxLines = 3,
		};
}
