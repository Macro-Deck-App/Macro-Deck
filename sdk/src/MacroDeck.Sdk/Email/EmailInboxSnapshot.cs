namespace MacroDeck.Sdk.Email;

/// <summary>What <see cref="IEmailInboxProvider.GetSnapshotAsync" /> returns: one inbox's status, counts and
/// newest messages.</summary>
public sealed record EmailInboxSnapshot
{
	/// <summary>Whether the data is current. Anything other than <see cref="EmailInboxStatus.Connected" />
	/// means the counts and messages are the last known ones.</summary>
	public required EmailInboxStatus Status { get; init; }

	/// <summary>Why the inbox is not connected, in the user's language, or <c>null</c>. Shown to the user,
	/// so it should say what to do, e.g. "Sign-in failed. Check the app password."</summary>
	public string? StatusMessage { get; init; }

	/// <summary>Unread messages in the whole inbox.</summary>
	public required int UnreadCount { get; init; }

	/// <summary>All messages in the whole inbox.</summary>
	public required int TotalCount { get; init; }

	/// <summary>Newest first, at most <see cref="EmailMessageQuery.MaxMessages" />. See
	/// <see cref="IEmailInboxProvider.GetSnapshotAsync" />.</summary>
	public IReadOnlyList<EmailMessage> Messages { get; init; } = [];

	/// <summary>When the provider last brought this data up to date with the mail server.</summary>
	public required DateTimeOffset SyncedAt { get; init; }
}
