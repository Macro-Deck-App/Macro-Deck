namespace MacroDeckHost.Application.Email;

// Every id and data key here is persisted in profiles and exports, so the spellings are frozen.
public static class EmailWidgetTypes
{
	public const string OwnerId = "app.macro-deck.email";

	public const string LocalId = "inbox";

	public const string QualifiedId = OwnerId + "::" + LocalId;

	public const string InboxesKey = "inboxes";

	public const string ModeKey = "mode";

	public const string ModeCount = "count";

	public const string ModeList = "list";

	public const string UnreadOnlyKey = "unreadOnly";

	public const string MaxMessagesKey = "maxMessages";

	public const string ShowSenderKey = "showSender";

	public const string ShowSubjectKey = "showSubject";

	public const string ShowPreviewKey = "showPreview";

	public const string ShowTimestampKey = "showTimestamp";

	public const int MinMessages = 1;

	public const int MaxMessages = 10;

	public const int DefaultMessages = 5;

	public const int MaxInboxesWithVariables = 40;

	public static bool IsEmailInboxWidget(string? widgetType)
	{
		return widgetType == QualifiedId;
	}
}
