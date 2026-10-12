namespace MacroDeck.Sdk.Email;

/// <summary>Says what <see cref="IEmailInboxProvider.Changed" /> is about.</summary>
/// <param name="inboxId">The <see cref="EmailInbox.Id" /> whose messages, counts or status changed, or
/// <c>null</c> when the inbox list itself changed.</param>
public sealed class EmailInboxChangedEventArgs(string? inboxId) : EventArgs
{
	/// <summary>The inbox whose messages, counts or status changed, or <c>null</c> when the inbox list
	/// changed.</summary>
	public string? InboxId { get; } = inboxId;
}
