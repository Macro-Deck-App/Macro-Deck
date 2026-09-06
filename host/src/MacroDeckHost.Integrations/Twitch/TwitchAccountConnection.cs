using System.Text.Json;
using MacroDeckHost.Integrations.Twitch.Auth;
using MacroDeckHost.Integrations.Twitch.Protocol;
using Serilog;

namespace MacroDeckHost.Integrations.Twitch;

internal sealed class TwitchAccountConnection : IDisposable
{
	private readonly ILogger _logger;
	private readonly TwitchEventEmitter? _emitter;
	private readonly Lock _sync = new();

	private volatile TwitchAccountState _state = TwitchAccountState.Unknown;
	private volatile IReadOnlyList<TwitchCustomReward> _rewards = [];
	private volatile IReadOnlyList<string> _missingScopeEvents = [];

	private TwitchEventSubSession? _session;
	private TwitchStatePoller? _poller;
	private bool _authorizationLost;

	public TwitchAccountConnection(
		TwitchAccount account,
		TwitchTokenProvider tokens,
		ITwitchOAuthClient oauthClient,
		ITwitchHelixClient helix,
		TwitchEventEmitter? emitter,
		ILogger logger)
	{
		Account = account;
		Tokens = tokens;
		OAuthClient = oauthClient;
		Helix = helix;
		_emitter = emitter;
		_logger = logger;
	}

	public TwitchAccount Account { get; }

	public TwitchTokenProvider Tokens { get; }

	public ITwitchOAuthClient OAuthClient { get; }

	public ITwitchHelixClient Helix { get; }

	public TwitchAccountState State => _state;

	public IReadOnlyList<TwitchCustomReward> Rewards => _rewards;

	public IReadOnlyList<string> MissingScopeEvents => _missingScopeEvents;

	public bool NeedsReauthorization => Tokens.NeedsReauthorization || _authorizationLost;

	public void Start(TwitchEventSubOptions? options = null, TwitchStatePollerOptions? pollerOptions = null)
	{
		var subscriptions = new TwitchSubscriptionManager(Helix, Account.UserId, () => Tokens.Scopes, _logger);

		_session = new TwitchEventSubSession(() => new TwitchEventSubClient(),
			new TwitchEventSubCallbacks(async (sessionId, cancellationToken) =>
				{
					var report = await subscriptions.SubscribeAllAsync(sessionId, cancellationToken);
					_missingScopeEvents = report.MissingScope;
					_logger.Information("Twitch account {Login} subscribed to {Count} event(s)",
						Account.Login,
						report.Created.Count);
				},
				OnNotification,
				(type, status) => _logger.Debug("Twitch revoked {Type}: {Status}", type, status),
				OnConnectionChanged,
				reason =>
				{
					_authorizationLost = true;
					_logger.Warning("Twitch authorization lost for {Login}: {Reason}", Account.Login, reason);
				}),
			_logger,
			options);

		_poller = new TwitchStatePoller(Helix, Account, Merge, AdoptRewards, _logger, pollerOptions);

		_session.Start();
		_poller.Start();
	}

	public TwitchAccountState Merge(Func<TwitchAccountState, TwitchAccountState> update)
	{
		lock (_sync)
		{
			var next = update(_state);
			_state = next;
			return next;
		}
	}

	public void Dispose()
	{
		_poller?.Dispose();
		_session?.Dispose();
		Tokens.Dispose();
		OAuthClient.Dispose();
	}

	public async Task StopAsync()
	{
		_poller?.Dispose();
		_session?.Dispose();
		await Tokens.StopAsync();
		Tokens.Dispose();
		OAuthClient.Dispose();
	}

	private void AdoptRewards(IReadOnlyList<TwitchCustomReward> rewards)
	{
		if (rewards.Count > 0)
		{
			_rewards = rewards;
		}
	}

	private void OnConnectionChanged(bool connected)
	{
		Merge(state => state with { IsConnected = connected });
		_emitter?.PublishConnection(Account, connected);
	}

	private void OnNotification(TwitchEventSubMessage message)
	{
		_emitter?.Publish(Account, message);
		ApplyToState(message);
	}

	private void ApplyToState(TwitchEventSubMessage message)
	{
		if (message.Payload.ValueKind is not JsonValueKind.Object ||
			!message.Payload.TryGetProperty("event", out var payload))
		{
			return;
		}

		switch (message.SubscriptionType)
		{
			case "stream.online":
				Merge(state => state with { IsLive = true, StreamStartedAt = ReadTime(payload, "started_at") });
				break;

			case "stream.offline":
				Merge(state => state with { IsLive = false, ViewerCount = 0, StreamStartedAt = null });
				break;

			case "channel.update":
				Merge(state => state with
				{
					StreamTitle = ReadString(payload, "title") ?? state.StreamTitle,
					StreamCategory = ReadString(payload, "category_name") ?? state.StreamCategory
				});

				break;

			case "channel.follow":
				Merge(state => state with
				{
					LastFollower = ReadString(payload, "user_name") ?? state.LastFollower,
					FollowerCount = state.FollowerCount + 1
				});

				break;

			case "channel.subscribe":
			case "channel.subscription.message":
				Merge(state => state with
				{
					LastSubscriber = ReadString(payload, "user_name") ?? state.LastSubscriber
				});

				break;

			case "channel.cheer":
				Merge(state => state with { LastCheerer = ReadString(payload, "user_name") ?? state.LastCheerer });
				break;

			case "channel.raid":
				if (string.Equals(ReadString(payload, "to_broadcaster_user_id"),
					Account.UserId,
					StringComparison.Ordinal))
				{
					Merge(state => state with
					{
						LastRaider = ReadString(payload, "from_broadcaster_user_name") ?? state.LastRaider
					});
				}

				break;

			case "channel.chat_settings.update":
				Merge(state => state with { ChatSettings = ReadChatSettings(payload, state.ChatSettings) });
				break;

			default:
				break;
		}
	}

	private static TwitchChatSettings ReadChatSettings(JsonElement payload, TwitchChatSettings? previous)
		=> new(ReadBool(payload, "emote_mode") ?? previous?.EmoteOnly,
			ReadBool(payload, "follower_mode") ?? previous?.FollowersOnly,
			ReadInt(payload, "follower_mode_duration_minutes") ?? previous?.FollowersOnlyDurationMinutes,
			ReadBool(payload, "slow_mode") ?? previous?.SlowMode,
			ReadInt(payload, "slow_mode_wait_time_seconds") ?? previous?.SlowModeWaitSeconds,
			ReadBool(payload, "subscriber_mode") ?? previous?.SubscriberOnly,
			ReadBool(payload, "unique_chat_mode") ?? previous?.UniqueChat);

	private static string? ReadString(JsonElement element, string property)
		=> element.TryGetProperty(property, out var value) && value.ValueKind is JsonValueKind.String
			? value.GetString()
			: null;

	private static bool? ReadBool(JsonElement element, string property)
		=> element.TryGetProperty(property, out var value) &&
			value.ValueKind is JsonValueKind.True or JsonValueKind.False
				? value.GetBoolean()
				: null;

	private static int? ReadInt(JsonElement element, string property)
		=> element.TryGetProperty(property, out var value) &&
			value.ValueKind is JsonValueKind.Number &&
			value.TryGetInt32(out var parsed)
				? parsed
				: null;

	private static DateTimeOffset? ReadTime(JsonElement element, string property)
		=> DateTimeOffset.TryParse(ReadString(element, property), out var parsed) ? parsed : null;
}
