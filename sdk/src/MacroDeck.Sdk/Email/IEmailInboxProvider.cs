namespace MacroDeck.Sdk.Email;

/// <summary>
/// Implemented by integrations that read email inboxes, from one or more accounts. The host merges the
/// inboxes of every enabled provider, so the email widget, variables and triggers show mail from several
/// services and accounts at once. Connecting and authorizing an account is not part of this contract: it
/// belongs to the integration's own configuration flow and issues.
/// </summary>
/// <remarks>
/// <para>
/// Read-only. The host never changes a mailbox through this interface. Later abilities, such as marking a
/// message as read, will be separate optional interfaces a provider may implement in addition; none is
/// required to take part.
/// </para>
/// <para>
/// The host caches what it reads and decides when to read again: after <see cref="Changed" />, after a
/// user's explicit refresh and on a slow fallback poll. A provider that pushes changes through
/// <see cref="Changed" /> is shown promptly; one that never raises it still works, minutes late.
/// </para>
/// </remarks>
public interface IEmailInboxProvider
{
	/// <summary>Human-readable provider name, e.g. "IMAP".</summary>
	/// <remarks>
	/// Optional. A provider that leaves this at the default is described by its integration's name
	/// instead - for a plugin, the <c>manifest.json</c> name. Stating a name here still wins.
	/// </remarks>
	string ProviderName => string.Empty;

	/// <summary>The inboxes this provider can currently read, typically one per configured account.</summary>
	/// <remarks>
	/// <para>
	/// Called often and synchronously, so return a cached list. Every <see cref="EmailInbox.Id" /> must be a
	/// valid resource local id (<c>MacroDeckId.IsValidLocalId(id, LocalIdKind.Resource)</c>), unique within
	/// the provider and stable across restarts, because the user's inbox selection, trigger filters and
	/// variable names are stored against it. The host skips an inbox that breaks these rules and logs a
	/// warning.
	/// </para>
	/// <para>
	/// The list holds only what identifies an inbox. Whether it is connected belongs in
	/// <see cref="EmailInboxSnapshot.Status" />, not here: an inbox that is temporarily unreachable stays
	/// in the list. When the list itself changes, raise <see cref="Changed" /> with a <c>null</c> inbox id.
	/// </para>
	/// </remarks>
	IReadOnlyList<EmailInbox> GetInboxes();

	/// <summary>The current counts and the newest messages of one inbox.</summary>
	/// <remarks>
	/// <para>
	/// <see cref="EmailInboxSnapshot.Messages" /> holds at most <see cref="EmailMessageQuery.MaxMessages" />
	/// messages, newest first. With <see cref="EmailMessageQuery.UnreadOnly" /> they are the newest unread
	/// messages wherever they are in the inbox, not the unread ones among the newest. The counts always
	/// describe the whole inbox, whatever the query.
	/// </para>
	/// <para>
	/// <b>Report what you know; throw only when you cannot answer at all.</b> A provider that knows it is
	/// not connected returns its last known counts and messages with <see cref="EmailInboxStatus.Disconnected" />
	/// or <see cref="EmailInboxStatus.Error" /> and a <see cref="EmailInboxSnapshot.StatusMessage" />. It
	/// throws when the read fails with nothing to return, and for an inbox id it does not know. The host
	/// keeps showing the last data it has after a thrown read and marks it as out of date. Honour
	/// <paramref name="cancellationToken" /> and let <see cref="OperationCanceledException" /> propagate.
	/// </para>
	/// </remarks>
	Task<EmailInboxSnapshot> GetSnapshotAsync(
		string inboxId,
		EmailMessageQuery query,
		CancellationToken cancellationToken);

	/// <summary>
	/// Raised when something a later read would answer differently has changed. May be raised on any
	/// thread, and as often as changes happen: the host coalesces bursts.
	/// </summary>
	/// <remarks>
	/// <para>
	/// With <see cref="EmailInboxChangedEventArgs.InboxId" /> set, that inbox's messages, counts or status
	/// changed and the host reads its snapshot again. With a <c>null</c> inbox id, the inbox list changed:
	/// the host calls <see cref="GetInboxes" /> again and then reads every inbox.
	/// </para>
	/// <para>
	/// In a plugin, the SDK forwards this event to the host for you. Calling
	/// <c>IPluginCatalogNotifier.CatalogChanged("email-inbox", inboxId)</c> directly has the same effect.
	/// </para>
	/// </remarks>
	event EventHandler<EmailInboxChangedEventArgs>? Changed;
}
