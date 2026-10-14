using System.Text.Json;
using MacroDeck.Localization;
using MacroDeck.Ui.Config;
using MacroDeck.Ui.Config.Options;
using MacroDeck.Ui.Dsl;
using MacroDeck.Ui.Runtime;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Configuration;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.EmailInbox.Config;

namespace MacroDeckHost.Widgets.EmailInbox;

internal static class EmailInboxWidgetConfigViews
{
	private const UiWidgetAppearanceFields Appearance = UiWidgetAppearanceFields.Border
		| UiWidgetAppearanceFields.BackgroundColor
		| UiWidgetAppearanceFields.TransparentBackground
		| UiWidgetAppearanceFields.ColorVariables;

	public static UiElement Build(JsonElement data, IEmailInboxCache cache)
	{
		ArgumentNullException.ThrowIfNull(cache);

		var settings = EmailInboxWidgetSettings.Parse(data);
		var inboxes = new UiState<IReadOnlyList<string>>(settings.Inboxes);
		var mode = new UiState<string>(settings.Mode);
		var unreadOnly = new UiState<bool>(settings.UnreadOnly);
		var maxMessages = new UiState<double>(settings.MaxMessages);
		var showSender = new UiState<bool>(settings.ShowSender);
		var showSubject = new UiState<bool>(settings.ShowSubject);
		var showPreview = new UiState<bool>(settings.ShowPreview);
		var showTimestamp = new UiState<bool>(settings.ShowTimestamp);
		var flows = new UiState<JsonElement>(WidgetConfigJson.ReadFlows(data));
		var snapshot = cache.Snapshot;

		return new UiWidgetConfiguration
		{
			Key = "root",
			Properties = new UiWidgetProperties
			{
				Key = "properties",
				Children =
				[
					new UiHeading { Key = "email-heading", Text = AppStrings.Widgets.EmailInbox.Name() },
					new UiMultiSelectInput
					{
						Key = EmailWidgetTypes.InboxesKey,
						Label = Strings.Inboxes(),
						Description = snapshot.Inboxes.Count == 0
							? Strings.NoInboxesHint()
							: Strings.InboxesDescription(),
						Binding = Bind.To(inboxes),
						Options = UiValue.Of<IReadOnlyList<UiOption>>(InboxOptions(snapshot))
					},
					new UiChoiceInput
					{
						Key = EmailWidgetTypes.ModeKey,
						Label = Strings.Mode(),
						Binding = Bind.To(mode),
						Options = UiValue.Of<IReadOnlyList<UiOption>>([
							UiOption.Of(EmailWidgetTypes.ModeCount, Strings.ModeCount()),
							UiOption.Of(EmailWidgetTypes.ModeList, Strings.ModeList())
						])
					},
					new UiHeading { Key = "display-heading", Text = AppStrings.Widgets.Editor.Display() },
					new UiBooleanInput
					{
						Key = EmailWidgetTypes.UnreadOnlyKey,
						Label = Strings.UnreadOnly(),
						Binding = Bind.To(unreadOnly),
						VisibleWhen = ForList(mode)
					},
					new UiNumberInput
					{
						Key = EmailWidgetTypes.MaxMessagesKey,
						Label = Strings.MaxMessages(),
						Description = Strings.MaxMessagesDescription(),
						Binding = Bind.To(maxMessages),
						Min = EmailWidgetTypes.MinMessages,
						Max = EmailWidgetTypes.MaxMessages,
						Step = 1,
						VisibleWhen = ForList(mode)
					},
					Toggle(EmailWidgetTypes.ShowSenderKey, Strings.ShowSender(), showSender, mode),
					Toggle(EmailWidgetTypes.ShowSubjectKey, Strings.ShowSubject(), showSubject, mode),
					Toggle(EmailWidgetTypes.ShowPreviewKey, Strings.ShowPreview(), showPreview, mode),
					Toggle(EmailWidgetTypes.ShowTimestampKey, Strings.ShowTimestamp(), showTimestamp, mode),
					UiWidgetAppearance.Section(data, Appearance)
				]
			},
			Editor = WidgetConfigFragments.FlowsEditor(flows)
		};
	}

	private static UiBooleanInput Toggle(string key, LocalizedText label, UiState<bool> value, UiState<string> mode)
	{
		return new UiBooleanInput { Key = key, Label = label, Binding = Bind.To(value), VisibleWhen = ForList(mode) };
	}

	private static UiVisibleWhen ForList(UiState<string> mode)
	{
		return new UiVisibleWhen
		{
			ParameterName = EmailWidgetTypes.ModeKey,
			Values = [EmailWidgetTypes.ModeList],
			SiblingValue = () => mode.Value
		};
	}

	private static List<UiOption> InboxOptions(EmailInboxesSnapshot snapshot)
	{
		return
		[
			.. snapshot.Inboxes.Select(state => new UiOption
			{
				Value = state.Ref,
				Label = state.Inbox.ProviderName.IsEmpty
					? state.Inbox.DisplayName
					: AppStrings.Events.Email.InboxOption(state.Inbox.DisplayName,
						state.Inbox.ProviderName)
			})
		];
	}
}
