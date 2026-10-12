namespace MacroDeck.Sdk.Email;

/// <summary>One message in an <see cref="EmailInboxSnapshot" />: its headers and a short preview, never the
/// full body.</summary>
public sealed record EmailMessage
{
	/// <summary>
	/// Unique within the inbox and stable across reads, otherwise opaque to the host. The host tells new
	/// mail from mail it has seen by this id, so an id that changes makes old mail look new.
	/// </summary>
	public required string Id { get; init; }

	/// <summary>The sender's display name, or <c>null</c> when the message has none.</summary>
	public string? SenderName { get; init; }

	/// <summary>The sender's address, or <c>null</c> when the message has none.</summary>
	public string? SenderAddress { get; init; }

	/// <summary>The subject. Empty when the message has none; the host then shows a placeholder.</summary>
	public required string Subject { get; init; }

	/// <summary>A short plain-text excerpt of the body, or <c>null</c> for none. The host cuts long text and
	/// shows it as plain text, so do not include markup.</summary>
	public string? Preview { get; init; }

	/// <summary>When the message arrived.</summary>
	public required DateTimeOffset ReceivedAt { get; init; }

	/// <summary>Whether the message is marked as read.</summary>
	public bool IsRead { get; init; }
}
