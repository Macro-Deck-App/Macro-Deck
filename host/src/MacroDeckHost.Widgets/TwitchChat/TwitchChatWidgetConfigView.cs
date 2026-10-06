using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Twitch.Chat;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.TwitchChat;

internal static class TwitchChatWidgetConfigView
{
	public static UiElement Build(JsonElement data, IReadOnlyList<TwitchChatAccount> accounts)
	{
		ArgumentNullException.ThrowIfNull(accounts);

		var account = new UiState<string>(WidgetConfigJson.ReadString(data, TwitchChatWidgetType.AccountKey) ??
			string.Empty);
		var allowModeration = new UiState<bool>(TwitchChatWidgetSettings.AllowsModeration(data));
		var fontSize = new UiState<double>(TwitchChatWidgetSettings.FontScale(data) * 100);
		var messageColor = new UiState<string>(
			WidgetConfigJson.ReadString(data, TwitchChatWidgetSettings.MessageColorKey) ?? string.Empty);
		var nameColor = new UiState<string>(
			WidgetConfigJson.ReadString(data, TwitchChatWidgetSettings.NameColorKey) ?? string.Empty);

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "chat-heading", Text = AppStrings.Integrations.Twitch.ChatWidget.Name() },
					new UiChoiceInput
					{
						Key = TwitchChatWidgetType.AccountKey,
						Label = AppStrings.Integrations.Twitch.Actions.AccountLabel(),
						Description = AppStrings.Integrations.Twitch.ChatWidget.AccountDescription(),
						Placeholder = AppStrings.Integrations.Twitch.ChatWidget.FirstAccount(),
						Binding = Bind.To(account),
						SupportsReset = true,
						DefaultValue = string.Empty,
						Options = UiValue.Of<IReadOnlyList<UiOption>>(
						[
							.. accounts.Select(candidate => UiOption.Of(candidate.UserId, candidate.Label)),
						]),
					},
					new UiBooleanInput
					{
						Key = TwitchChatWidgetType.AllowModerationKey,
						Label = AppStrings.Integrations.Twitch.ChatDialog.AllowModeration(),
						Description = AppStrings.Integrations.Twitch.ChatDialog.AllowModerationDescription(),
						Binding = Bind.To(allowModeration),
					},
					new UiNumberInput
					{
						Key = TwitchChatWidgetSettings.FontSizeKey,
						Label = MacroDeckStrings.Widgets.Appearance.FontSize(),
						Min = TwitchChatWidgetSettings.MinFontSizePercent,
						Max = TwitchChatWidgetSettings.MaxFontSizePercent,
						Step = 5,
						Binding = Bind.To(fontSize),
					},
					new UiColorInput
					{
						Key = TwitchChatWidgetSettings.MessageColorKey,
						Label = AppStrings.Integrations.Twitch.ChatWidget.MessageColor(),
						Binding = Bind.To(messageColor),
						SupportsReset = true,
						DefaultValue = string.Empty,
					},
					new UiColorInput
					{
						Key = TwitchChatWidgetSettings.NameColorKey,
						Label = AppStrings.Integrations.Twitch.ChatWidget.NameColor(),
						Description = AppStrings.Integrations.Twitch.ChatWidget.NameColorDescription(),
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
