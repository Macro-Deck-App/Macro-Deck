namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal interface ITwitchHelixClient
{
	Task<TwitchSubscriptionResult> CreateEventSubSubscriptionAsync(
		string type,
		string version,
		IReadOnlyDictionary<string, string> condition,
		string sessionId,
		CancellationToken cancellationToken);

	Task<TwitchStreamInfo> GetStreamAsync(string broadcasterId, CancellationToken cancellationToken);

	Task<TwitchChannelInfo> GetChannelAsync(string broadcasterId, CancellationToken cancellationToken);

	Task<int?> GetFollowerCountAsync(string broadcasterId, CancellationToken cancellationToken);

	Task<TwitchSubscriberInfo> GetSubscriberInfoAsync(string broadcasterId, CancellationToken cancellationToken);

	Task<TwitchChatSettings> GetChatSettingsAsync(string broadcasterId, CancellationToken cancellationToken);

	Task<IReadOnlyList<TwitchCustomReward>> GetCustomRewardsAsync(
		string broadcasterId,
		CancellationToken cancellationToken);

	Task<TwitchUserInfo?> GetUserAsync(string? userId, string? login, CancellationToken cancellationToken);

	Task ModifyChannelAsync(
		string broadcasterId,
		string? title,
		string? categoryId,
		IReadOnlyList<string>? tags,
		CancellationToken cancellationToken);

	Task<string?> ResolveCategoryIdAsync(string name, CancellationToken cancellationToken);

	Task SendChatMessageAsync(
		string broadcasterId,
		string senderId,
		string message,
		string? replyToMessageId,
		CancellationToken cancellationToken);

	Task SendAnnouncementAsync(
		string broadcasterId,
		string moderatorId,
		string message,
		string color,
		CancellationToken cancellationToken);

	Task SendShoutoutAsync(
		string fromBroadcasterId,
		string toBroadcasterId,
		string moderatorId,
		CancellationToken cancellationToken);

	Task DeleteChatMessagesAsync(
		string broadcasterId,
		string moderatorId,
		string? messageId,
		CancellationToken cancellationToken);

	Task SetChatModeAsync(
		string broadcasterId,
		string moderatorId,
		TwitchChatMode mode,
		bool enabled,
		int? duration,
		CancellationToken cancellationToken);

	Task StartCommercialAsync(string broadcasterId, int lengthSeconds, CancellationToken cancellationToken);

	Task SnoozeNextAdAsync(string broadcasterId, CancellationToken cancellationToken);

	Task StartRaidAsync(string fromBroadcasterId, string toBroadcasterId, CancellationToken cancellationToken);

	Task CancelRaidAsync(string broadcasterId, CancellationToken cancellationToken);

	Task<TwitchCreatedClip?> CreateClipAsync(string broadcasterId, bool hasDelay, CancellationToken cancellationToken);

	Task<bool> ClipExistsAsync(string clipId, CancellationToken cancellationToken);

	Task CreateStreamMarkerAsync(string broadcasterId, string? description, CancellationToken cancellationToken);

	Task BanUserAsync(
		string broadcasterId,
		string moderatorId,
		string targetUserId,
		int? durationSeconds,
		string? reason,
		CancellationToken cancellationToken);

	Task UnbanUserAsync(
		string broadcasterId,
		string moderatorId,
		string targetUserId,
		CancellationToken cancellationToken);

	Task CreatePollAsync(
		string broadcasterId,
		string title,
		IReadOnlyList<string> choices,
		int durationSeconds,
		int? channelPointsPerVote,
		CancellationToken cancellationToken);

	Task<TwitchActiveEvent?> GetActivePollAsync(string broadcasterId, CancellationToken cancellationToken);

	Task EndPollAsync(string broadcasterId, string pollId, bool archive, CancellationToken cancellationToken);

	Task CreatePredictionAsync(
		string broadcasterId,
		string title,
		IReadOnlyList<string> outcomes,
		int windowSeconds,
		CancellationToken cancellationToken);

	Task<TwitchActiveEvent?> GetActivePredictionAsync(string broadcasterId, CancellationToken cancellationToken);

	Task EndPredictionAsync(
		string broadcasterId,
		string predictionId,
		string status,
		string? winningOutcomeId,
		CancellationToken cancellationToken);

	Task UpdateRedemptionStatusAsync(
		string broadcasterId,
		string rewardId,
		string redemptionId,
		bool fulfilled,
		CancellationToken cancellationToken);
}
