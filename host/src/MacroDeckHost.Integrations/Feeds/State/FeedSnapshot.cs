namespace MacroDeckHost.Integrations.Feeds.State;

internal sealed record FeedSnapshot(Guid EntryId, string Title, string VariableKey)
{
	public string? Group { get; init; }

	public bool AllowLocalNetwork { get; init; }

	public string? FeedTitle { get; init; }

	public IReadOnlyList<FeedItem> Items { get; init; } = [];

	public IReadOnlyList<string> Unread { get; init; } = [];

	public string Status { get; init; } = FeedStatus.Pending;

	public string? Error { get; init; }

	public DateTimeOffset? LastUpdated { get; init; }

	public int UnreadCount => Items.Count(item => Unread.Contains(item.Key, StringComparer.Ordinal));

	public FeedItem? Find(string key)
	{
		return Items.FirstOrDefault(item => string.Equals(item.Key, key, StringComparison.Ordinal));
	}

	public bool IsUnread(string key)
	{
		return Unread.Contains(key, StringComparer.Ordinal);
	}
}
