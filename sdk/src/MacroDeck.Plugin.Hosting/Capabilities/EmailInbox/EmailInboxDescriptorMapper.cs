using System.Text.Json;
using MacroDeck.Plugin.Protocol.Capabilities.EmailInbox;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Email;

namespace MacroDeck.Plugin.Hosting.Capabilities.EmailInbox;

internal static class EmailInboxDescriptorMapper
{
	private const int ResultFramingBytes = 64;

	public static EmailInboxDto ToDto(Sdk.Email.EmailInbox inbox)
	{
		return new EmailInboxDto
		{
			Id = inbox.Id,
			DisplayName = Clip(inbox.DisplayName, ProtocolLimits.MaxEmailSenderLength),
			Address = ClipOptional(inbox.Address, ProtocolLimits.MaxEmailSenderLength)
		};
	}

	public static EmailInboxSnapshotResult ToSnapshotResult(EmailInboxSnapshot snapshot, int maxMessages)
	{
		var result = new EmailInboxSnapshotResult
		{
			Status = Enum.IsDefined(snapshot.Status)
				? snapshot.Status.ToString()
				: nameof(EmailInboxStatus.Disconnected),
			StatusMessage = ClipOptional(snapshot.StatusMessage, ProtocolLimits.MaxEmailSubjectLength),
			UnreadCount = Math.Max(0, snapshot.UnreadCount),
			TotalCount = Math.Max(0, snapshot.TotalCount),
			Messages = [],
			SyncedAt = snapshot.SyncedAt
		};

		var budget = ProtocolLimits.MaxEmailInboxReplyBytes - ResultFramingBytes - SerializedSize(result);
		var used = 0;
		var messages = new List<EmailMessageDto>();
		var truncated = false;

		foreach (var message in snapshot.Messages.OrderByDescending(m => m.ReceivedAt).Take(maxMessages))
		{
			var dto = ToDto(message);
			var size = SerializedSize(dto) + 1;

			if (used + size > budget)
			{
				truncated = true;
				break;
			}

			used += size;
			messages.Add(dto);
		}

		return result with { Messages = messages, Truncated = truncated };
	}

	private static EmailMessageDto ToDto(EmailMessage message)
	{
		return new EmailMessageDto
		{
			Id = message.Id,
			SenderName = ClipOptional(message.SenderName, ProtocolLimits.MaxEmailSenderLength),
			SenderAddress = ClipOptional(message.SenderAddress, ProtocolLimits.MaxEmailSenderLength),
			Subject = Clip(message.Subject, ProtocolLimits.MaxEmailSubjectLength),
			Preview = ClipOptional(message.Preview, ProtocolLimits.MaxEmailPreviewLength),
			ReceivedAt = message.ReceivedAt,
			IsRead = message.IsRead
		};
	}

	private static int SerializedSize<T>(T value)
	{
		return JsonSerializer.SerializeToUtf8Bytes(value, PluginProtocolJson.Options).Length;
	}

	private static string? ClipOptional(string? text, int maxLength)
	{
		return text is null ? null : Clip(text, maxLength);
	}

	private static string Clip(string text, int maxLength)
	{
		if (text.Length <= maxLength)
		{
			return text;
		}

		var length = char.IsHighSurrogate(text[maxLength - 1]) ? maxLength - 1 : maxLength;
		return text[..length];
	}
}
