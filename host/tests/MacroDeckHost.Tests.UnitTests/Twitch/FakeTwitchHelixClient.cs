using MacroDeckHost.Integrations.Twitch;
using MacroDeckHost.Integrations.Twitch.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

internal sealed class FakeTwitchHelixClient : ITwitchHelixClient
{
	private readonly Lock _sync = new();

	public List<(string Type, string Version, IReadOnlyDictionary<string, string> Condition, string SessionId)>
		Subscriptions { get; } = [];

	public List<string> Calls { get; } = [];

	public Dictionary<string, TwitchSubscriptionResult> SubscriptionResults { get; } = new(StringComparer.Ordinal);

	public TwitchStreamInfo Stream { get; set; } = new(false, 0, null, null, null);

	public TwitchChannelInfo Channel { get; set; } = new(null, null, null, null);

	public int? FollowerCount { get; set; }

	public TwitchSubscriberInfo Subscribers { get; set; } = new(null, null);

	public TwitchChatSettings ChatSettings { get; set; } = new(null, null, null, null, null, null, null);

	public IReadOnlyList<TwitchCustomReward> Rewards { get; set; } = [];

	public TwitchUserInfo? User { get; set; }

	public string? CategoryId { get; set; }

	public string? ClipId { get; set; } = "clip-1";

	public string ClipEditUrl { get; set; } = "https://clips.twitch.tv/edit/abc";

	public int ClipConfirmedAfterPolls { get; set; } = 1;

	public int ClipPollCount { get; private set; }

	public TwitchActiveEvent? ActivePoll { get; set; }

	public TwitchActiveEvent? ActivePrediction { get; set; }

	public HashSet<string> FailingReads { get; } = new(StringComparer.Ordinal);

	public Dictionary<string, Exception> FailingReadExceptions { get; } = new(StringComparer.Ordinal);

	public Task<TwitchSubscriptionResult> CreateEventSubSubscriptionAsync(
		string type,
		string version,
		IReadOnlyDictionary<string, string> condition,
		string sessionId,
		CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			Subscriptions.Add((type, version, condition, sessionId));
		}

		return Task.FromResult(SubscriptionResults.GetValueOrDefault(type, TwitchSubscriptionResult.Created));
	}

	public Task<TwitchStreamInfo> GetStreamAsync(string broadcasterId, CancellationToken cancellationToken)
		=> Read("stream", Stream);

	public Task<TwitchChannelInfo> GetChannelAsync(string broadcasterId, CancellationToken cancellationToken)
		=> Read("channel", Channel);

	public Task<int?> GetFollowerCountAsync(string broadcasterId, CancellationToken cancellationToken)
		=> Read("followers", FollowerCount);

	public Task<TwitchSubscriberInfo> GetSubscriberInfoAsync(string broadcasterId, CancellationToken cancellationToken)
		=> Read("subscribers", Subscribers);

	public Task<TwitchChatSettings> GetChatSettingsAsync(string broadcasterId, CancellationToken cancellationToken)
		=> Read("chatSettings", ChatSettings);

	public Task<IReadOnlyList<TwitchCustomReward>> GetCustomRewardsAsync(
		string broadcasterId,
		CancellationToken cancellationToken)
		=> Read("rewards", Rewards);

	public Task<TwitchUserInfo?> GetUserAsync(string? userId, string? login, CancellationToken cancellationToken)
		=> Read("user", User);

	public Task ModifyChannelAsync(
		string broadcasterId,
		string? title,
		string? categoryId,
		IReadOnlyList<string>? tags,
		CancellationToken cancellationToken)
		=> Record($"modifyChannel:{title}|{categoryId}|{(tags is null ? string.Empty : string.Join('+', tags))}");

	public Task<string?> ResolveCategoryIdAsync(string name, CancellationToken cancellationToken)
	{
		Record($"resolveCategory:{name}");
		return Task.FromResult(CategoryId);
	}

	public Task SendChatMessageAsync(
		string broadcasterId,
		string senderId,
		string message,
		string? replyToMessageId,
		CancellationToken cancellationToken)
		=> Record($"chat:{message}|{replyToMessageId}");

	public Task SendAnnouncementAsync(
		string broadcasterId,
		string moderatorId,
		string message,
		string color,
		CancellationToken cancellationToken)
		=> Record($"announce:{message}|{color}");

	public Task SendShoutoutAsync(
		string fromBroadcasterId,
		string toBroadcasterId,
		string moderatorId,
		CancellationToken cancellationToken)
		=> Record($"shoutout:{toBroadcasterId}");

	public Task DeleteChatMessagesAsync(
		string broadcasterId,
		string moderatorId,
		string? messageId,
		CancellationToken cancellationToken)
		=> Record($"deleteChat:{messageId}");

	public Task SetChatModeAsync(
		string broadcasterId,
		string moderatorId,
		TwitchChatMode mode,
		bool enabled,
		int? duration,
		CancellationToken cancellationToken)
		=> Record($"chatMode:{mode}|{enabled}|{duration}");

	public Task StartCommercialAsync(string broadcasterId, int lengthSeconds, CancellationToken cancellationToken)
		=> Record($"commercial:{lengthSeconds}");

	public Task SnoozeNextAdAsync(string broadcasterId, CancellationToken cancellationToken)
		=> Record("snoozeAd");

	public Task StartRaidAsync(string fromBroadcasterId, string toBroadcasterId, CancellationToken cancellationToken)
		=> Record($"raid:{toBroadcasterId}");

	public Task CancelRaidAsync(string broadcasterId, CancellationToken cancellationToken)
		=> Record("cancelRaid");

	public Task<TwitchCreatedClip?> CreateClipAsync(
		string broadcasterId,
		bool hasDelay,
		CancellationToken cancellationToken)
	{
		Record($"clip:{hasDelay}");
		return Task.FromResult(ClipId is null ? null : new TwitchCreatedClip(ClipId, ClipEditUrl));
	}

	public Task<bool> ClipExistsAsync(string clipId, CancellationToken cancellationToken)
	{
		lock (_sync)
		{
			Calls.Add($"getClips:{clipId}");
		}

		if (FailingReads.Contains("getClips"))
		{
			return Task.FromException<bool>(new TwitchRequestException("getClips failed"));
		}

		ClipPollCount++;
		return Task.FromResult(ClipPollCount >= ClipConfirmedAfterPolls);
	}

	public Task CreateStreamMarkerAsync(
		string broadcasterId,
		string? description,
		CancellationToken cancellationToken)
		=> Record($"marker:{description}");

	public Task BanUserAsync(
		string broadcasterId,
		string moderatorId,
		string targetUserId,
		int? durationSeconds,
		string? reason,
		CancellationToken cancellationToken)
		=> Record($"ban:{targetUserId}|{durationSeconds}|{reason}");

	public Task UnbanUserAsync(
		string broadcasterId,
		string moderatorId,
		string targetUserId,
		CancellationToken cancellationToken)
		=> Record($"unban:{targetUserId}");

	public Task CreatePollAsync(
		string broadcasterId,
		string title,
		IReadOnlyList<string> choices,
		int durationSeconds,
		int? channelPointsPerVote,
		CancellationToken cancellationToken)
		=> Record($"createPoll:{title}|{string.Join('+', choices)}|{durationSeconds}|{channelPointsPerVote}");

	public Task<TwitchActiveEvent?> GetActivePollAsync(string broadcasterId, CancellationToken cancellationToken)
	{
		Record("activePoll");
		return Task.FromResult(ActivePoll);
	}

	public Task EndPollAsync(string broadcasterId, string pollId, bool archive, CancellationToken cancellationToken)
		=> Record($"endPoll:{pollId}|{archive}");

	public Task CreatePredictionAsync(
		string broadcasterId,
		string title,
		IReadOnlyList<string> outcomes,
		int windowSeconds,
		CancellationToken cancellationToken)
		=> Record($"createPrediction:{title}|{string.Join('+', outcomes)}|{windowSeconds}");

	public Task<TwitchActiveEvent?> GetActivePredictionAsync(
		string broadcasterId,
		CancellationToken cancellationToken)
	{
		Record("activePrediction");
		return Task.FromResult(ActivePrediction);
	}

	public Task EndPredictionAsync(
		string broadcasterId,
		string predictionId,
		string status,
		string? winningOutcomeId,
		CancellationToken cancellationToken)
		=> Record($"endPrediction:{predictionId}|{status}|{winningOutcomeId}");

	public Task UpdateRedemptionStatusAsync(
		string broadcasterId,
		string rewardId,
		string redemptionId,
		bool fulfilled,
		CancellationToken cancellationToken)
		=> Record($"redemption:{rewardId}|{redemptionId}|{fulfilled}");

	private Task Record(string call)
	{
		lock (_sync)
		{
			Calls.Add(call);
		}

		return Task.CompletedTask;
	}

	private Task<T> Read<T>(string name, T value)
	{
		lock (_sync)
		{
			Calls.Add(name);
		}

		if (!FailingReads.Contains(name))
		{
			return Task.FromResult(value);
		}

		var exception = FailingReadExceptions.TryGetValue(name, out var custom)
			? custom
			: new TwitchRequestException($"{name} failed");
		return Task.FromException<T>(exception);
	}
}
