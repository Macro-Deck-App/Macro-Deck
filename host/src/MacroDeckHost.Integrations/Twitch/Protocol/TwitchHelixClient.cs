using System.Globalization;
using System.Text.Json;
using MacroDeckHost.Integrations.Twitch.Auth;
using Serilog;
using TwitchLib.Api;
using TwitchLib.Api.Core;
using TwitchLib.Api.Core.Enums;
using TwitchLib.Api.Core.Exceptions;
using TwitchLib.Api.Core.HttpCallHandlers;
using TwitchLib.Api.Helix.Models.Channels.ModifyChannelInformation;
using TwitchLib.Api.Helix.Models.Channels.SendChatMessage;
using TwitchLib.Api.Helix.Models.Channels.StartCommercial;
using TwitchLib.Api.Helix.Models.Chat;
using TwitchLib.Api.Helix.Models.ChannelPoints.UpdateCustomRewardRedemptionStatus;
using TwitchLib.Api.Helix.Models.Moderation.BanUser;
using TwitchLib.Api.Helix.Models.Polls.CreatePoll;
using TwitchLib.Api.Helix.Models.Predictions.CreatePrediction;
using TwitchLib.Api.Helix.Models.Streams.CreateStreamMarker;
using ChatSettingsRequest = TwitchLib.Api.Helix.Models.Chat.ChatSettings.ChatSettings;

namespace MacroDeckHost.Integrations.Twitch.Protocol;

internal sealed class TwitchHelixClient : ITwitchHelixClient
{
	private const int RateLimitAttempts = 3;

	private static readonly TimeSpan _rateLimitBackoff = TimeSpan.FromSeconds(2);

	private static readonly TwitchHttpClient _sharedHttp = new();

	private readonly TwitchAPI _api;
	private readonly TwitchTokenProvider _tokens;
	private readonly ILogger _logger;
	private readonly Func<TimeSpan, CancellationToken, Task> _delay;
	private readonly TwitchHttpClient _http;

	public TwitchHelixClient(
		TwitchAPI api,
		TwitchTokenProvider tokens,
		ILogger logger,
		Func<TimeSpan, CancellationToken, Task>? delay = null,
		TwitchHttpClient? httpClient = null)
	{
		_api = api;
		_tokens = tokens;
		_logger = logger;
		_delay = delay ?? Task.Delay;

		_http = httpClient ?? _sharedHttp;
	}

	public static TwitchAPI CreateApi(string clientId)
		=> new(settings: new ApiSettings
		{
			ClientId = clientId,

			SkipDynamicScopeValidation = true,

			SkipAutoServerTokenGeneration = true
		});

	public Task<TwitchSubscriptionResult> CreateEventSubSubscriptionAsync(
		string type,
		string version,
		IReadOnlyDictionary<string, string> condition,
		string sessionId,
		CancellationToken cancellationToken)
		=> CreateSubscriptionAsync(type, version, condition, sessionId, cancellationToken);

	public Task<TwitchStreamInfo> GetStreamAsync(string broadcasterId, CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				var response = await _api.Helix.Streams.GetStreamsAsync(userIds: [broadcasterId], accessToken: token);
				var stream = response?.Streams?.FirstOrDefault();

				return stream is null
					? new TwitchStreamInfo(false, 0, null, null, null)
					: new TwitchStreamInfo(true,
						stream.ViewerCount,
						stream.Title,
						stream.GameName,
						stream.StartedAt);
			},
			cancellationToken);

	public Task<TwitchChannelInfo> GetChannelAsync(string broadcasterId, CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				var response = await _api.Helix.Channels.GetChannelInformationAsync(broadcasterId, token);
				var channel = response?.Data?.FirstOrDefault();

				return new TwitchChannelInfo(channel?.Title,
					channel?.GameId,
					channel?.GameName,
					channel?.BroadcasterLanguage);
			},
			cancellationToken);

	public Task<int?> GetFollowerCountAsync(string broadcasterId, CancellationToken cancellationToken)
		=> CallAsync<int?>(async token =>
			{
				var response = await _api.Helix.Channels.GetChannelFollowersAsync(broadcasterId,
					first: 1,
					accessToken: token);

				return response?.Total;
			},
			cancellationToken);

	public Task<TwitchSubscriberInfo> GetSubscriberInfoAsync(string broadcasterId, CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				var response = await _api.Helix.Subscriptions.GetBroadcasterSubscriptionsAsync(broadcasterId,
					first: 1,
					accessToken: token);

				return new TwitchSubscriberInfo(response?.Total, response?.Points);
			},
			cancellationToken);

	public Task<TwitchChatSettings> GetChatSettingsAsync(string broadcasterId, CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				var response = await _api.Helix.Chat.GetChatSettingsAsync(broadcasterId, broadcasterId, token);
				var settings = response?.Data?.FirstOrDefault();

				return new TwitchChatSettings(settings?.EmoteMode,
					settings?.FollowerMode,
					settings?.FollowerModeDuration,
					settings?.SlowMode,
					settings?.SlowModeWaitDuration,
					settings?.SubscriberMode,
					settings?.UniqueChatMode);
			},
			cancellationToken);

	public Task<IReadOnlyList<TwitchCustomReward>> GetCustomRewardsAsync(
		string broadcasterId,
		CancellationToken cancellationToken)
		=> CallAsync<IReadOnlyList<TwitchCustomReward>>(async token =>
			{
				var response = await _api.Helix.ChannelPoints.GetCustomRewardAsync(broadcasterId, accessToken: token);

				return response?.Data?.Select(reward => new TwitchCustomReward(reward.Id, reward.Title)).ToList() ?? [];
			},
			cancellationToken);

	public Task<TwitchUserInfo?> GetUserAsync(string? userId, string? login, CancellationToken cancellationToken)
		=> CallAsync<TwitchUserInfo?>(async token =>
			{
				var response = await _api.Helix.Users.GetUsersAsync(ids: userId is null ? null : [userId],
					logins: login is null ? null : [login],
					accessToken: token);

				var user = response?.Users?.FirstOrDefault();

				return user is null ? null : new TwitchUserInfo(user.Id, user.Login, user.DisplayName);
			},
			cancellationToken);

	public Task ModifyChannelAsync(
		string broadcasterId,
		string? title,
		string? categoryId,
		IReadOnlyList<string>? tags,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				var request = new ModifyChannelInformationRequest();
				if (!string.IsNullOrEmpty(title))
				{
					request.Title = title;
				}

				if (!string.IsNullOrEmpty(categoryId))
				{
					request.GameId = categoryId;
				}

				if (tags is { Count: > 0 })
				{
					request.Tags = [.. tags];
				}

				await _api.Helix.Channels.ModifyChannelInformationAsync(broadcasterId, request, token);
				return true;
			},
			cancellationToken);

	public Task<string?> ResolveCategoryIdAsync(string name, CancellationToken cancellationToken)
		=> CallAsync<string?>(async token =>
			{
				var exact = await _api.Helix.Games.GetGamesAsync(gameNames: [name], accessToken: token);
				var match = exact?.Data?.FirstOrDefault();
				if (match is not null)
				{
					return match.Id;
				}

				var search = await _api.Helix.Search.SearchCategoriesAsync(name, first: 1, accessToken: token);
				return search?.Games?.FirstOrDefault()?.Id;
			},
			cancellationToken);

	public Task SendChatMessageAsync(
		string broadcasterId,
		string senderId,
		string message,
		string? replyToMessageId,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Chat.SendChatMessage(new SendChatMessageRequest
					{
						BroadcasterId = broadcasterId,
						SenderId = senderId,
						Message = message,
						ReplyParentMessageId = string.IsNullOrEmpty(replyToMessageId) ? null : replyToMessageId
					},
					token);

				return true;
			},
			cancellationToken);

	public Task SendAnnouncementAsync(
		string broadcasterId,
		string moderatorId,
		string message,
		string color,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Chat.SendChatAnnouncementAsync(broadcasterId,
					moderatorId,
					message,
					ParseAnnouncementColor(color),
					token);

				return true;
			},
			cancellationToken);

	public Task SendShoutoutAsync(
		string fromBroadcasterId,
		string toBroadcasterId,
		string moderatorId,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Chat.SendShoutoutAsync(fromBroadcasterId, toBroadcasterId, moderatorId, token);
				return true;
			},
			cancellationToken);

	public Task DeleteChatMessagesAsync(
		string broadcasterId,
		string moderatorId,
		string? messageId,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Moderation.DeleteChatMessagesAsync(broadcasterId,
					moderatorId,
					string.IsNullOrEmpty(messageId) ? null : messageId,
					token);

				return true;
			},
			cancellationToken);

	public Task SetChatModeAsync(
		string broadcasterId,
		string moderatorId,
		TwitchChatMode mode,
		bool enabled,
		int? duration,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				var settings = new ChatSettingsRequest();
				switch (mode)
				{
					case TwitchChatMode.EmoteOnly:
						settings.EmoteMode = enabled;
						break;

					case TwitchChatMode.FollowersOnly:
						settings.FollowerMode = enabled;
						if (enabled && duration is { } minutes)
						{
							settings.FollowerModeDuration = minutes;
						}

						break;

					case TwitchChatMode.SlowMode:
						settings.SlowMode = enabled;
						if (enabled && duration is { } seconds)
						{
							settings.SlowModeWaitTime = seconds;
						}

						break;

					case TwitchChatMode.SubscribersOnly:
						settings.SubscriberMode = enabled;
						break;

					case TwitchChatMode.UniqueChat:
						settings.UniqueChatMode = enabled;
						break;

					default:
						throw new TwitchRequestException($"Unknown chat mode '{mode}'.");
				}

				await _api.Helix.Chat.UpdateChatSettingsAsync(broadcasterId, moderatorId, settings, token);
				return true;
			},
			cancellationToken);

	public Task StartCommercialAsync(string broadcasterId, int lengthSeconds, CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Channels.StartCommercialAsync(
					new StartCommercialRequest { BroadcasterId = broadcasterId, Length = lengthSeconds },
					token);

				return true;
			},
			cancellationToken);

	public Task SnoozeNextAdAsync(string broadcasterId, CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Channels.SnoozeNextAdAsync(broadcasterId, token);
				return true;
			},
			cancellationToken);

	public Task StartRaidAsync(string fromBroadcasterId, string toBroadcasterId, CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Raids.StartRaidAsync(fromBroadcasterId, toBroadcasterId, token);
				return true;
			},
			cancellationToken);

	public Task CancelRaidAsync(string broadcasterId, CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Raids.CancelRaidAsync(broadcasterId, token);
				return true;
			},
			cancellationToken);

	public Task<TwitchCreatedClip?> CreateClipAsync(
		string broadcasterId,
		bool hasDelay,
		CancellationToken cancellationToken)
		=> CallAsync<TwitchCreatedClip?>(async token =>
			{
				var url = string.Create(CultureInfo.InvariantCulture,
					$"https://api.twitch.tv/helix/clips?broadcaster_id={Uri.EscapeDataString(broadcasterId)}" +
					$"&has_delay={(hasDelay ? "true" : "false")}");

				var (_, body) = await _http.GeneralRequestAsync(url,
					"POST",
					api: ApiVersion.Helix,
					clientId: _api.Settings.ClientId,
					accessToken: token);

				using var document = JsonDocument.Parse(body);
				var clips = document.RootElement.GetProperty("data");
				if (clips.GetArrayLength() == 0)
				{
					return null;
				}

				var clip = clips[0];
				return new TwitchCreatedClip(clip.GetProperty("id").GetString() ?? string.Empty,
					clip.GetProperty("edit_url").GetString() ?? string.Empty);
			},
			cancellationToken);

	public Task<bool> ClipExistsAsync(string clipId, CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				var response = await _api.Helix.Clips.GetClipsAsync(clipIds: [clipId], accessToken: token);
				return response?.Clips?.Any(clip => string.Equals(clip.Id, clipId, StringComparison.Ordinal)) ?? false;
			},
			cancellationToken);

	public Task CreateStreamMarkerAsync(
		string broadcasterId,
		string? description,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Streams.CreateStreamMarkerAsync(
					new CreateStreamMarkerRequest { UserId = broadcasterId, Description = description },
					token);

				return true;
			},
			cancellationToken);

	public Task BanUserAsync(
		string broadcasterId,
		string moderatorId,
		string targetUserId,
		int? durationSeconds,
		string? reason,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				var request = new BanUserRequest { UserId = targetUserId, Reason = reason ?? string.Empty };

				if (durationSeconds is > 0)
				{
					request.Duration = durationSeconds;
				}

				await _api.Helix.Moderation.BanUserAsync(broadcasterId, moderatorId, request, token);
				return true;
			},
			cancellationToken);

	public Task UnbanUserAsync(
		string broadcasterId,
		string moderatorId,
		string targetUserId,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Moderation.UnbanUserAsync(broadcasterId, moderatorId, targetUserId, token);
				return true;
			},
			cancellationToken);

	public Task CreatePollAsync(
		string broadcasterId,
		string title,
		IReadOnlyList<string> choices,
		int durationSeconds,
		int? channelPointsPerVote,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				var request = new CreatePollRequest
				{
					BroadcasterId = broadcasterId,
					Title = title,
					Choices = [.. choices.Select(choice => new Choice { Title = choice })],
					DurationSeconds = durationSeconds
				};

				if (channelPointsPerVote is > 0)
				{
					request.ChannelPointsVotingEnabled = true;
					request.ChannelPointsPerVote = channelPointsPerVote.Value;
				}

				await _api.Helix.Polls.CreatePollAsync(request, token);
				return true;
			},
			cancellationToken);

	public Task<TwitchActiveEvent?> GetActivePollAsync(string broadcasterId, CancellationToken cancellationToken)
		=> CallAsync<TwitchActiveEvent?>(async token =>
			{
				var response = await _api.Helix.Polls.GetPollsAsync(broadcasterId, first: 1, accessToken: token);
				var poll = response?.Data?.FirstOrDefault();

				return poll is null
					? null
					: new TwitchActiveEvent(poll.Id,
						poll.Title,
						poll.Choices?.Select(choice => new TwitchNamedOutcome(choice.Id, choice.Title)).ToList() ?? []);
			},
			cancellationToken);

	public Task EndPollAsync(string broadcasterId, string pollId, bool archive, CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Polls.EndPollAsync(broadcasterId,
					pollId,
					archive ? PollStatusEnum.ARCHIVED : PollStatusEnum.TERMINATED,
					token);

				return true;
			},
			cancellationToken);

	public Task CreatePredictionAsync(
		string broadcasterId,
		string title,
		IReadOnlyList<string> outcomes,
		int windowSeconds,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Predictions.CreatePredictionAsync(new CreatePredictionRequest
					{
						BroadcasterId = broadcasterId,
						Title = title,
						Outcomes = [.. outcomes.Select(outcome => new Outcome { Title = outcome })],
						PredictionWindowSeconds = windowSeconds
					},
					token);

				return true;
			},
			cancellationToken);

	public Task<TwitchActiveEvent?> GetActivePredictionAsync(string broadcasterId, CancellationToken cancellationToken)
		=> CallAsync<TwitchActiveEvent?>(async token =>
			{
				var response
					= await _api.Helix.Predictions.GetPredictionsAsync(broadcasterId, first: 1, accessToken: token);

				var prediction = response?.Data?.FirstOrDefault();

				return prediction is null
					? null
					: new TwitchActiveEvent(prediction.Id,
						prediction.Title,
						prediction.Outcomes?.Select(outcome => new TwitchNamedOutcome(outcome.Id, outcome.Title))
							.ToList() ??
						[]);
			},
			cancellationToken);

	public Task EndPredictionAsync(
		string broadcasterId,
		string predictionId,
		string status,
		string? winningOutcomeId,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.Predictions.EndPredictionAsync(broadcasterId,
					predictionId,
					ParsePredictionStatus(status),
					string.IsNullOrEmpty(winningOutcomeId) ? null : winningOutcomeId,
					token);

				return true;
			},
			cancellationToken);

	public Task UpdateRedemptionStatusAsync(
		string broadcasterId,
		string rewardId,
		string redemptionId,
		bool fulfilled,
		CancellationToken cancellationToken)
		=> CallAsync(async token =>
			{
				await _api.Helix.ChannelPoints.UpdateRedemptionStatusAsync(broadcasterId,
					rewardId,
					[redemptionId],
					new UpdateCustomRewardRedemptionStatusRequest
					{
						Status = fulfilled
							? CustomRewardRedemptionStatus.FULFILLED
							: CustomRewardRedemptionStatus.CANCELED
					},
					token);

				return true;
			},
			cancellationToken);

	private async Task<TwitchSubscriptionResult> CreateSubscriptionAsync(
		string type,
		string version,
		IReadOnlyDictionary<string, string> condition,
		string sessionId,
		CancellationToken cancellationToken)
	{
		try
		{
			await CallAsync(async token =>
				{
					await _api.Helix.EventSub.CreateEventSubSubscriptionAsync(type,
						version,
						condition.ToDictionary(StringComparer.Ordinal),
						EventSubTransportMethod.Websocket,
						sessionId,
						accessToken: token);

					return true;
				},
				cancellationToken);

			return TwitchSubscriptionResult.Created;
		}
		catch (TwitchScopeException)
		{
			return TwitchSubscriptionResult.MissingScope;
		}
		catch (BadRequestException ex) when (IsDuplicate(ex))
		{
			// A reconnect race can ask twice; an existing subscription is as good as a created one.
			return TwitchSubscriptionResult.Duplicate;
		}
		catch (Exception ex) when (ex is BadRequestException or BadParameterException)
		{
			_logger.Error(ex, "Twitch rejected the subscription {Type} v{Version}", type, version);
			return TwitchSubscriptionResult.Unsupported;
		}
		catch (Exception ex) when (ex is not OperationCanceledException and not TwitchOAuthRejectedException)
		{
			_logger.Warning(ex, "Could not create the Twitch subscription {Type} v{Version}", type, version);
			return TwitchSubscriptionResult.Failed;
		}
	}

	private static bool IsDuplicate(Exception exception)
		=> exception.Message.Contains("already exists", StringComparison.OrdinalIgnoreCase) ||
			exception.Message.Contains("conflict", StringComparison.OrdinalIgnoreCase);

	private async Task<T> CallAsync<T>(Func<string, Task<T>> call, CancellationToken cancellationToken)
	{
		var refreshed = false;

		for (var attempt = 1;; attempt++)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				return await call(await _tokens.GetAsync(cancellationToken));
			}
			catch (Exception ex) when (!refreshed &&
				ex is TokenExpiredException or BadTokenException or InvalidCredentialException)
			{
				refreshed = true;
				await _tokens.ForceRefreshAsync(cancellationToken);
			}
			catch (BadScopeException ex)
			{
				throw new TwitchScopeException("The Twitch account did not grant the permission for this call.", ex);
			}
			catch (TooManyRequestsException) when (attempt < RateLimitAttempts)
			{
				await _delay(_rateLimitBackoff * attempt, cancellationToken);
			}
		}
	}

	private static AnnouncementColors ParseAnnouncementColor(string color)
		=> color.ToLowerInvariant() switch
		{
			"blue" => AnnouncementColors.Blue,
			"green" => AnnouncementColors.Green,
			"orange" => AnnouncementColors.Orange,
			"purple" => AnnouncementColors.Purple,
			_ => AnnouncementColors.Primary
		};

	private static PredictionEndStatus ParsePredictionStatus(string status)
		=> status.ToUpperInvariant() switch
		{
			"CANCELED" => PredictionEndStatus.CANCELED,
			"LOCKED" => PredictionEndStatus.LOCKED,
			_ => PredictionEndStatus.RESOLVED
		};
}
