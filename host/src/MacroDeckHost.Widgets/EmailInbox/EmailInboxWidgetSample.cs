using MacroDeck.Localization;
using MacroDeck.Sdk.Email;
using MacroDeckHost.Application.Email;
using MacroDeckHost.Localization;
using MacroDeckHost.Widgets.Preview;
using Strings = MacroDeckHost.Localization.AppStrings.Widgets.EmailInbox.Sample;

namespace MacroDeckHost.Widgets.EmailInbox;

internal static class EmailInboxWidgetSample
{
	private const string InboxId = "sample";

	public static async Task<EmailInboxesSnapshot> BuildAsync(IWidgetSampleTextResolver text, DateTimeOffset now)
	{
		ArgumentNullException.ThrowIfNull(text);

		var inboxName = await text.ResolveAsync(Strings.Inbox()).ConfigureAwait(false);

		async Task<EmailMessage> Message(
			string id,
			LocalizedString sender,
			LocalizedString subject,
			LocalizedString preview,
			TimeSpan age,
			bool isRead)
		{
			return new EmailMessage
			{
				Id = id,
				SenderName = await text.ResolveAsync(sender).ConfigureAwait(false),
				Subject = await text.ResolveAsync(subject).ConfigureAwait(false),
				Preview = await text.ResolveAsync(preview).ConfigureAwait(false),
				ReceivedAt = now - age,
				IsRead = isRead
			};
		}

		IReadOnlyList<EmailMessage> messages =
		[
			await Message("team",
					Strings.TeamSender(),
					Strings.TeamSubject(),
					Strings.TeamPreview(),
					TimeSpan.FromMinutes(4),
					false)
				.ConfigureAwait(false),
			await Message("invoice",
					Strings.InvoiceSender(),
					Strings.InvoiceSubject(),
					Strings.InvoicePreview(),
					TimeSpan.FromMinutes(52),
					false)
				.ConfigureAwait(false),
			await Message("friend",
					Strings.FriendSender(),
					Strings.FriendSubject(),
					Strings.FriendPreview(),
					TimeSpan.FromHours(3),
					true)
				.ConfigureAwait(false)
		];

		var inbox = new EmailInboxDescriptor(EmailWidgetTypes.OwnerId + "::" + InboxId,
			EmailWidgetTypes.OwnerId,
			InboxId,
			AppStrings.Integrations.Email.Name(),
			inboxName,
			"you@example.com");

		return new EmailInboxesSnapshot([
			new EmailInboxState
			{
				Inbox = inbox,
				Slug = InboxId,
				Status = EmailInboxStatus.Connected,
				UnreadCount = 2,
				TotalCount = 3,
				Latest = messages,
				LatestUnread = [.. messages.Where(message => !message.IsRead)],
				SyncedAt = now
			}
		]);
	}
}
