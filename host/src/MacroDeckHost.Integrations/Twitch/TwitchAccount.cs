namespace MacroDeckHost.Integrations.Twitch;

internal sealed record TwitchAccount(
	Guid EntryId,
	string ClientId,
	string UserId,
	string Login,
	string DisplayName,
	string VariableKey,
	DateTimeOffset ConnectedAt)
{
	public string Label => string.Equals(DisplayName, Login, StringComparison.OrdinalIgnoreCase)
		? DisplayName
		: $"{DisplayName} (@{Login})";
}

internal sealed record TwitchAccountState
{
	public static TwitchAccountState Unknown { get; } = new();

	public bool IsConnected { get; init; }

	public bool? IsLive { get; init; }

	public string? StreamTitle { get; init; }

	public string? StreamCategory { get; init; }

	public int? ViewerCount { get; init; }

	public DateTimeOffset? StreamStartedAt { get; init; }

	public int? FollowerCount { get; init; }

	public int? SubscriberCount { get; init; }

	public int? SubscriberPoints { get; init; }

	public TwitchChatSettings? ChatSettings { get; init; }

	public string? LastFollower { get; init; }

	public string? LastSubscriber { get; init; }

	public string? LastCheerer { get; init; }

	public string? LastRaider { get; init; }
}

internal sealed record TwitchChatSettings(
	bool? EmoteOnly,
	bool? FollowersOnly,
	int? FollowersOnlyDurationMinutes,
	bool? SlowMode,
	int? SlowModeWaitSeconds,
	bool? SubscriberOnly,
	bool? UniqueChat);
