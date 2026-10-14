using MacroDeck.Localization;
using MacroDeck.Sdk.Email;

namespace MacroDeckHost.Application.Email;

public sealed record EmailInboxDescriptor(
	string Ref,
	string IntegrationId,
	string LocalId,
	LocalizedText ProviderName,
	string DisplayName,
	string? Address);

public sealed record EmailInboxState
{
	public required EmailInboxDescriptor Inbox { get; init; }

	public required string Slug { get; init; }

	public EmailInboxStatus Status { get; init; } = EmailInboxStatus.Connecting;

	public string? StatusMessage { get; init; }

	public int UnreadCount { get; init; }

	public int TotalCount { get; init; }

	public IReadOnlyList<EmailMessage> Latest { get; init; } = [];

	public IReadOnlyList<EmailMessage> LatestUnread { get; init; } = [];

	public DateTimeOffset? SyncedAt { get; init; }

	public bool IsStale { get; init; }

	public bool HasData => SyncedAt is not null;

	public string Ref => Inbox.Ref;

	public bool IsConnected => Status == EmailInboxStatus.Connected;

	public bool SameContentAs(EmailInboxState? other)
	{
		return other is not null
			&& Inbox == other.Inbox
			&& Slug == other.Slug
			&& Status == other.Status
			&& StatusMessage == other.StatusMessage
			&& UnreadCount == other.UnreadCount
			&& TotalCount == other.TotalCount
			&& SyncedAt == other.SyncedAt
			&& IsStale == other.IsStale
			&& Latest.SequenceEqual(other.Latest)
			&& LatestUnread.SequenceEqual(other.LatestUnread);
	}
}

public sealed record EmailInboxesSnapshot(IReadOnlyList<EmailInboxState> Inboxes)
{
	public static readonly EmailInboxesSnapshot Empty = new([]);

	public EmailInboxState? Find(string inboxRef)
	{
		return Inboxes.FirstOrDefault(state => string.Equals(state.Ref, inboxRef, StringComparison.Ordinal));
	}

	public bool SameContentAs(EmailInboxesSnapshot other)
	{
		return Inboxes.Count == other.Inboxes.Count
			&& Inboxes.Zip(other.Inboxes).All(pair => pair.First.SameContentAs(pair.Second));
	}

	public EmailInboxAggregate Aggregate(IReadOnlyCollection<string> inboxRefs)
	{
		var selected = inboxRefs.Count == 0
			? Inboxes
			: [.. Inboxes.Where(state => inboxRefs.Contains(state.Ref, StringComparer.Ordinal))];

		return new EmailInboxAggregate(selected,
			selected.Sum(state => state.UnreadCount),
			selected.Sum(state => state.TotalCount),
			Merge(selected.Select(state => (state, state.Latest))),
			Merge(selected.Select(state => (state, state.LatestUnread))));
	}

	private static IReadOnlyList<EmailInboxMessage> Merge(
		IEnumerable<(EmailInboxState Inbox, IReadOnlyList<EmailMessage> Messages)> sources)
	{
		return
		[
			.. sources
				.SelectMany(source
					=> source.Messages.Select(message => new EmailInboxMessage(source.Inbox.Inbox, message)))
				.OrderByDescending(entry => entry.Message.ReceivedAt)
				.ThenBy(entry => entry.Inbox.Ref, StringComparer.Ordinal)
				.Take(EmailInboxCache.WindowSize)
		];
	}
}

public sealed record EmailInboxMessage(EmailInboxDescriptor Inbox, EmailMessage Message);

public sealed record EmailInboxAggregate(
	IReadOnlyList<EmailInboxState> Inboxes,
	int UnreadCount,
	int TotalCount,
	IReadOnlyList<EmailInboxMessage> Latest,
	IReadOnlyList<EmailInboxMessage> LatestUnread)
{
	public bool AllConnected => Inboxes.All(state => state.IsConnected && !state.IsStale);
}
