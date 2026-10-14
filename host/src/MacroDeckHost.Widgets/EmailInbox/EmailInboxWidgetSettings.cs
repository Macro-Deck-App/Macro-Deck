using System.Text.Json;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Widgets.Configuration;

namespace MacroDeckHost.Widgets.EmailInbox;

internal sealed record EmailInboxWidgetSettings(
	IReadOnlyList<string> Inboxes,
	string Mode,
	bool UnreadOnly,
	int MaxMessages,
	bool ShowSender,
	bool ShowSubject,
	bool ShowPreview,
	bool ShowTimestamp,
	string? BackgroundColor)
{
	public bool IsList => Mode == EmailWidgetTypes.ModeList;

	public static EmailInboxWidgetSettings Parse(JsonElement data)
	{
		return new EmailInboxWidgetSettings(ReadInboxes(data),
			WidgetConfigJson.ReadString(data, EmailWidgetTypes.ModeKey) == EmailWidgetTypes.ModeList
				? EmailWidgetTypes.ModeList
				: EmailWidgetTypes.ModeCount,
			WidgetConfigJson.ReadBool(data, EmailWidgetTypes.UnreadOnlyKey) ?? false,
			WidgetConfigJson.ReadDouble(data, EmailWidgetTypes.MaxMessagesKey) is { } count
				? (int)Math.Clamp(Math.Round(count), EmailWidgetTypes.MinMessages, EmailWidgetTypes.MaxMessages)
				: EmailWidgetTypes.DefaultMessages,
			WidgetConfigJson.ReadBool(data, EmailWidgetTypes.ShowSenderKey) ?? true,
			WidgetConfigJson.ReadBool(data, EmailWidgetTypes.ShowSubjectKey) ?? true,
			WidgetConfigJson.ReadBool(data, EmailWidgetTypes.ShowPreviewKey) ?? false,
			WidgetConfigJson.ReadBool(data, EmailWidgetTypes.ShowTimestampKey) ?? true,
			WidgetColor.NormalizeBackground(WidgetConfigJson.ReadString(data, "backgroundColor")));
	}

	private static IReadOnlyList<string> ReadInboxes(JsonElement data)
	{
		if (data.ValueKind != JsonValueKind.Object
			|| !data.TryGetProperty(EmailWidgetTypes.InboxesKey, out var inboxes)
			|| inboxes.ValueKind != JsonValueKind.Array)
		{
			return [];
		}

		return
		[
			.. inboxes.EnumerateArray()
				.Where(item => item.ValueKind == JsonValueKind.String)
				.Select(item => item.GetString()!)
				.Where(inboxRef => inboxRef.Length > 0)
				.Distinct(StringComparer.Ordinal)
		];
	}
}
