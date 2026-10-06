using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.StreamChat;

internal static class StreamChatWidgetConfigView
{
	public static UiElement Build(StreamPlatform platform, JsonElement data, IReadOnlyList<ChatAccount> accounts)
	{
		ArgumentNullException.ThrowIfNull(platform);
		ArgumentNullException.ThrowIfNull(accounts);

		var account = new UiState<string>(WidgetConfigJson.ReadString(data, StreamChatWidgetType.AccountKey) ??
			string.Empty);
		var allowModeration = new UiState<bool>(StreamChatWidgetSettings.AllowsModeration(data));
		var fontSize = new UiState<double>(StreamChatWidgetSettings.FontScale(data) * 100);
		var messageColor = new UiState<string>(
			WidgetConfigJson.ReadString(data, StreamChatWidgetSettings.MessageColorKey) ?? string.Empty);
		var nameColor = new UiState<string>(
			WidgetConfigJson.ReadString(data, StreamChatWidgetSettings.NameColorKey) ?? string.Empty);

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "chat-heading", Text = platform.Chat.Heading() },
					new UiChoiceInput
					{
						Key = StreamChatWidgetType.AccountKey,
						Label = platform.AccountLabel(),
						Description = platform.Chat.AccountDescription(),
						Placeholder = AppStrings.Integrations.StreamChat.Widget.FirstAccount(),
						Binding = Bind.To(account),
						SupportsReset = true,
						DefaultValue = string.Empty,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. accounts.Select(candidate => UiOption.Of(candidate.AccountId, candidate.Label)),
						]),
					},
					new UiBooleanInput
					{
						Key = StreamChatWidgetType.AllowModerationKey,
						Label = AppStrings.Integrations.StreamChat.Dialog.AllowModeration(),
						Description = AppStrings.Integrations.StreamChat.Dialog.AllowModerationDescription(),
						Binding = Bind.To(allowModeration),
					},
					new UiNumberInput
					{
						Key = StreamChatWidgetSettings.FontSizeKey,
						Label = MacroDeckStrings.Widgets.Appearance.FontSize(),
						Min = StreamChatWidgetSettings.MinFontSizePercent,
						Max = StreamChatWidgetSettings.MaxFontSizePercent,
						Step = 5,
						Binding = Bind.To(fontSize),
					},
					new UiColorInput
					{
						Key = StreamChatWidgetSettings.MessageColorKey,
						Label = AppStrings.Integrations.StreamChat.Widget.MessageColor(),
						Binding = Bind.To(messageColor),
						SupportsReset = true,
						DefaultValue = string.Empty,
					},
					new UiColorInput
					{
						Key = StreamChatWidgetSettings.NameColorKey,
						Label = AppStrings.Integrations.StreamChat.Widget.NameColor(),
						Description = AppStrings.Integrations.StreamChat.Widget.NameColorDescription(),
						Binding = Bind.To(nameColor),
						SupportsReset = true,
						DefaultValue = string.Empty,
					},
					UiWidgetAppearance.Section(data,
						UiWidgetAppearanceFields.Border | UiWidgetAppearanceFields.BackgroundColor |
						UiWidgetAppearanceFields.TransparentBackground),
				],
			},
		};
	}
}
