namespace MacroDeck.Sdk.Email;

/// <summary>
/// One inbox an <see cref="IEmailInboxProvider" /> reads, e.g. the INBOX folder of one account. See
/// <see cref="IEmailInboxProvider.GetInboxes" /> for the rules <see cref="Id" /> has to follow.
/// </summary>
public sealed record EmailInbox
{
	/// <summary>Unique within the provider and stable across restarts.</summary>
	public required string Id { get; init; }

	/// <summary>Shown to the user, e.g. "Work". The host derives the inbox's variable names from it once,
	/// so renaming an inbox later does not rename its variables.</summary>
	public required string DisplayName { get; init; }

	/// <summary>The account's email address, or <c>null</c> when the provider has none to show.</summary>
	public string? Address { get; init; }
}
