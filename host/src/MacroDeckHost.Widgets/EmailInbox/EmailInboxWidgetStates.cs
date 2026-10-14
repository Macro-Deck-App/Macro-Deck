using System.Security.Cryptography;
using System.Text;
using MacroDeck.Localization;
using MacroDeck.Sdk.Email;
using MacroDeckHost.Application.Email;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.EmailInbox;

namespace MacroDeckHost.Widgets.EmailInbox;

internal enum EmailInboxWidgetStatus
{
	NoInboxes,

	Ready
}

internal sealed record EmailInboxComputed(EmailInboxWidgetState State, DateTimeOffset NextRefresh);

internal sealed record EmailInboxRow(
	string Key,
	bool IsUnread,
	LocalizedText Title,
	LocalizedText Subtitle,
	string? Preview,
	EmailInboxTime Time);

internal sealed record EmailInboxWidgetState(
	EmailInboxWidgetStatus Status,
	bool HasData,
	int UnreadCount,
	LocalizedText Count,
	LocalizedText Unread,
	LocalizedText Title,
	string? InboxName,
	LocalizedText Problem,
	LocalizedText EmptyText,
	IReadOnlyList<EmailInboxRow> Rows)
{
	public static EmailInboxWidgetState None { get; } = new(EmailInboxWidgetStatus.NoInboxes,
		false,
		0,
		default,
		default,
		default,
		null,
		default,
		default,
		[]);

	public bool HasProblem => !Problem.IsEmpty;

	public bool ShowsEmpty => HasData && Rows.Count == 0;

	public bool HasUnread => HasData && UnreadCount > 0;
}

internal static class EmailInboxWidget
{
	private const string NoData = "–";

	public static EmailInboxComputed Compute(
		EmailInboxesSnapshot snapshot,
		DateTimeOffset now,
		EmailInboxWidgetSettings settings,
		EmailInboxFormat format)
	{
		var aggregate = snapshot.Aggregate(settings.Inboxes);
		var nextRefresh = format.NextMidnight(now);

		if (aggregate.Inboxes.Count == 0)
		{
			return new EmailInboxComputed(EmailInboxWidgetState.None, nextRefresh);
		}

		var hasData = aggregate.Inboxes.Any(state => state.HasData);
		var single = aggregate.Inboxes.Count == 1 ? aggregate.Inboxes[0].Inbox.DisplayName : null;
		var messages = settings.UnreadOnly ? aggregate.LatestUnread : aggregate.Latest;
		var problems = aggregate.Inboxes.Count(state =>
			state.IsStale || state.Status is EmailInboxStatus.Disconnected or EmailInboxStatus.Error);

		var state = new EmailInboxWidgetState(EmailInboxWidgetStatus.Ready,
			hasData,
			aggregate.UnreadCount,
			hasData ? format.Count(aggregate.UnreadCount) : NoData,
			Strings.UnreadCount(aggregate.UnreadCount),
			single is null ? Strings.AllInboxes() : single,
			single,
			problems == 0 ? default
			: aggregate.Inboxes.Count == 1 ? Problem(aggregate.Inboxes[0])
			: Strings.SomeDisconnected(),
			settings.UnreadOnly ? Strings.NoUnreadEmails() : Strings.NoEmails(),
			[.. messages.Take(settings.MaxMessages).Select(message => Row(message, settings, format, now))]);

		return new EmailInboxComputed(state, nextRefresh);
	}

	private static LocalizedText Problem(EmailInboxState inbox)
	{
		return string.IsNullOrWhiteSpace(inbox.StatusMessage) ? Strings.Disconnected() : inbox.StatusMessage;
	}

	private static EmailInboxRow Row(
		EmailInboxMessage entry,
		EmailInboxWidgetSettings settings,
		EmailInboxFormat format,
		DateTimeOffset now)
	{
		var message = entry.Message;
		var sender = message.SenderName ?? message.SenderAddress ?? (LocalizedText)Strings.UnknownSender();
		LocalizedText subject = string.IsNullOrWhiteSpace(message.Subject) ? Strings.NoSubject() : message.Subject;

		return new EmailInboxRow(KeyOf(entry),
			!message.IsRead,
			settings.ShowSender ? sender : settings.ShowSubject ? subject : default,
			settings is { ShowSender: true, ShowSubject: true } ? subject : default,
			settings.ShowPreview ? SingleSpaced(message.Preview) : null,
			settings.ShowTimestamp ? format.Received(message.ReceivedAt, now) : EmailInboxTime.None);
	}

	private static string? SingleSpaced(string? text)
	{
		return string.IsNullOrWhiteSpace(text)
			? null
			: string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
	}

	private static string KeyOf(EmailInboxMessage entry)
	{
		return "m"
			+ Convert.ToHexStringLower(
				SHA256.HashData(Encoding.UTF8.GetBytes(entry.Inbox.Ref + "\n" + entry.Message.Id)))[..24];
	}
}
