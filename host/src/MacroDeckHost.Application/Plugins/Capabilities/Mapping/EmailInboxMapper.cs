using MacroDeck.Plugin.Protocol.Capabilities.EmailInbox;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Email;
using MacroDeckHost.Application.Email;

namespace MacroDeckHost.Application.Plugins.Capabilities.Mapping;

// Every text a plugin sends is clipped again here: the plugin is untrusted, and the SDK's own clipping
// only binds a plugin built against it.
public static class EmailInboxMapper
{
	public static EmailInbox ToDomain(EmailInboxDto dto)
	{
		return new EmailInbox
		{
			Id = dto.Id,
			DisplayName = EmailText.Clip(dto.DisplayName, ProtocolLimits.MaxEmailSenderLength),
			Address = EmailText.ClipOptional(dto.Address, ProtocolLimits.MaxEmailSenderLength)
		};
	}

	public static EmailInboxDto ToDto(EmailInbox inbox)
	{
		return new EmailInboxDto { Id = inbox.Id, DisplayName = inbox.DisplayName, Address = inbox.Address };
	}

	public static IReadOnlyList<EmailInbox> ToDomain(IEnumerable<EmailInboxDto> inboxes)
	{
		return [.. inboxes.Take(ProtocolLimits.MaxEmailInboxes).Select(ToDomain)];
	}

	public static EmailInboxSnapshot ToDomain(EmailInboxSnapshotResult result, int maxMessages)
	{
		return new EmailInboxSnapshot
		{
			Status = ToStatus(result.Status),
			StatusMessage = EmailText.ClipOptional(result.StatusMessage, ProtocolLimits.MaxEmailSubjectLength),
			UnreadCount = Math.Max(0, result.UnreadCount),
			TotalCount = Math.Max(0, result.TotalCount),
			Messages =
			[
				.. result.Messages.Take(Math.Clamp(maxMessages, 1, ProtocolLimits.MaxEmailMessages))
					.Select(ToDomain)
			],
			SyncedAt = result.SyncedAt
		};
	}

	private static EmailMessage ToDomain(EmailMessageDto dto)
	{
		return new EmailMessage
		{
			Id = dto.Id,
			SenderName = EmailText.ClipOptional(dto.SenderName, ProtocolLimits.MaxEmailSenderLength),
			SenderAddress = EmailText.ClipOptional(dto.SenderAddress, ProtocolLimits.MaxEmailSenderLength),
			Subject = EmailText.Clip(dto.Subject, ProtocolLimits.MaxEmailSubjectLength),
			Preview = EmailText.ClipOptional(dto.Preview, ProtocolLimits.MaxEmailPreviewLength),
			ReceivedAt = dto.ReceivedAt,
			IsRead = dto.IsRead
		};
	}

	// Matched by name rather than through Enum.TryParse, which also accepts "3" and a comma list. A status
	// this build has no name for reads as Disconnected, as the protocol documents.
	private static EmailInboxStatus ToStatus(string? status)
	{
		return status switch
		{
			nameof(EmailInboxStatus.Connecting) => EmailInboxStatus.Connecting,
			nameof(EmailInboxStatus.Connected) => EmailInboxStatus.Connected,
			nameof(EmailInboxStatus.Error) => EmailInboxStatus.Error,
			_ => EmailInboxStatus.Disconnected
		};
	}
}
