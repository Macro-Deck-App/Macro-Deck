namespace MacroDeckHost.Integrations.YouTube;

internal sealed record YouTubeAccount(
	Guid EntryId,
	string ClientId,
	string ChannelId,
	string Title,
	string? Handle,
	string VariableKey,
	DateTimeOffset ConnectedAt)
{
	public string Label
		=> Handle is { Length: > 0 } handle &&
			!string.Equals(Title, handle.TrimStart('@'), StringComparison.OrdinalIgnoreCase)
				? $"{Title} (@{handle.TrimStart('@')})"
				: Title;
}

internal sealed record YouTubeAccountState
{
	public static YouTubeAccountState Unknown { get; } = new();

	public bool? IsLive { get; init; }

	public string? ChannelTitle { get; init; }

	public string? BroadcastId { get; init; }

	public string? LiveChatId { get; init; }

	public string? StreamTitle { get; init; }

	public long? ViewerCount { get; init; }

	public long? LikeCount { get; init; }

	public long? SubscriberCount { get; init; }

	public DateTimeOffset? StreamStartedAt { get; init; }

	public string? StreamThumbnailUrl { get; init; }
}
