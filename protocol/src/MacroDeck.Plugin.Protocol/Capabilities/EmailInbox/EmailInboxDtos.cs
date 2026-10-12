namespace MacroDeck.Plugin.Protocol.Capabilities.EmailInbox;

/// <summary>Mirrors the SDK's <c>EmailInbox</c>, with the display name and address cut to
/// <c>ProtocolLimits.MaxEmailSenderLength</c>.</summary>
public sealed record EmailInboxDto
{
	public required string Id { get; init; }

	public required string DisplayName { get; init; }

	public string? Address { get; init; }
}

/// <summary>The full result of the <c>email-inbox</c> capability's <c>describe</c> operation: the provider
/// name and at most <c>ProtocolLimits.MaxEmailInboxes</c> inboxes.</summary>
public sealed record EmailInboxDescribePayload
{
	public required string ProviderName { get; init; }

	public required IReadOnlyList<EmailInboxDto> Inboxes { get; init; }
}

/// <summary>Result of the <c>inboxes</c> operation: the inbox list <c>describe</c> carries, as its own
/// narrow round trip. It is what the host reads after a <c>state.update</c> for the kind without a local
/// id.</summary>
public sealed record EmailInboxesResult
{
	public required IReadOnlyList<EmailInboxDto> Inboxes { get; init; }
}

/// <summary>Arguments for the <c>snapshot</c> operation: the SDK's <c>EmailMessageQuery</c> plus the inbox
/// it addresses. Inbox ids are config-entry shaped and do not exist at declaration time - see
/// <c>ProviderCapabilityId</c>'s remarks.</summary>
public sealed record EmailInboxSnapshotArguments
{
	public required string InboxId { get; init; }

	/// <summary>From 1 to <c>ProtocolLimits.MaxEmailMessages</c>. A value outside that range is clamped
	/// into it.</summary>
	public int MaxMessages { get; init; } = 10;

	/// <summary>Only the newest unread messages, wherever they are in the inbox.</summary>
	public bool UnreadOnly { get; init; }
}

/// <summary>Mirrors the SDK's <c>EmailMessage</c>. The subject is cut to
/// <c>ProtocolLimits.MaxEmailSubjectLength</c>, the preview to <c>ProtocolLimits.MaxEmailPreviewLength</c>
/// and the sender's name and address to <c>ProtocolLimits.MaxEmailSenderLength</c>.</summary>
public sealed record EmailMessageDto
{
	public required string Id { get; init; }

	public string? SenderName { get; init; }

	public string? SenderAddress { get; init; }

	public required string Subject { get; init; }

	public string? Preview { get; init; }

	public required DateTimeOffset ReceivedAt { get; init; }

	public bool IsRead { get; init; }
}

/// <summary>Result of the <c>snapshot</c> operation. <see cref="Status" /> is a string, not the SDK's
/// <c>EmailInboxStatus</c> enum - see <c>ActionParameterDto</c>'s remarks.</summary>
public sealed record EmailInboxSnapshotResult
{
	/// <summary>One of the SDK's <c>EmailInboxStatus</c> member names. A name the reader does not know reads
	/// as <c>Disconnected</c>.</summary>
	public required string Status { get; init; }

	/// <summary>Cut to <c>ProtocolLimits.MaxEmailSubjectLength</c>.</summary>
	public string? StatusMessage { get; init; }

	public required int UnreadCount { get; init; }

	public required int TotalCount { get; init; }

	/// <summary>Newest first, at most the requested <c>MaxMessages</c>.</summary>
	public required IReadOnlyList<EmailMessageDto> Messages { get; init; }

	public required DateTimeOffset SyncedAt { get; init; }

	/// <summary>True when the oldest messages were dropped to keep the reply within
	/// <c>ProtocolLimits.MaxEmailInboxReplyBytes</c>.</summary>
	public bool Truncated { get; init; }
}
