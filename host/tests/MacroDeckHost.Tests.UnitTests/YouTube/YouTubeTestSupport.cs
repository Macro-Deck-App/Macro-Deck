using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using MacroDeck.Sdk.Events;
using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Auth;
using MacroDeckHost.Integrations.YouTube.Protocol;
using MacroDeckHost.Tests.UnitTests.Twitch;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

internal static class YouTubeTestSupport
{
	public static readonly DateTimeOffset Now = new(2026, 10, 5, 19, 0, 0, TimeSpan.Zero);

	public static ILogger Silent { get; } = Logger.None;

	public static YouTubePollerOptions ManualOptions(TimeProvider? time = null)
		=> new()
		{
			Delay = (_, cancellationToken) => Task.Delay(Timeout.Infinite, cancellationToken),
			Time = time ?? new YouTubeManualClock(Now)
		};

	public static Guid AddChannel(
		RecordingIntegrationConfig config,
		string channelId,
		string title,
		string? handle = null,
		string clientId = "client-1.apps.googleusercontent.com",
		DateTimeOffset? connectedAt = null,
		string? entryTitle = null,
		string? dailyQuota = null)
		=> config.AddEntry(entryTitle ?? $"YouTube ({title})",
			new Dictionary<string, string?>(StringComparer.Ordinal)
			{
				[YouTubeConfigKeys.ClientId] = clientId,
				[YouTubeConfigKeys.ChannelId] = channelId,
				[YouTubeConfigKeys.ChannelTitle] = title,
				[YouTubeConfigKeys.ChannelHandle] = handle,
				[YouTubeConfigKeys.DailyQuota] = dailyQuota,
				[YouTubeConfigKeys.Scopes] = YouTubeOAuthEndpoints.Scope,
				[YouTubeConfigKeys.ExpiresAt] = Now.AddHours(1).ToString("o", CultureInfo.InvariantCulture),
				[YouTubeConfigKeys.ConnectedAt] = connectedAt?.ToString("o", CultureInfo.InvariantCulture)
			},
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				[YouTubeConfigKeys.ClientSecret] = "client-secret",
				[YouTubeConfigKeys.AccessToken] = "access-" + channelId,
				[YouTubeConfigKeys.RefreshToken] = "refresh-" + channelId
			});

	public static YouTubeAccountManager Manager(
		Func<YouTubeAccount, IYouTubeApiClient> api,
		YouTubeQuotaBudgets? budgets = null,
		YouTubePollerOptions? options = null)
		=> new(() => new FakeYouTubeOAuthClient(),
			Silent,
			(account, _, _) => api(account),
			budgets ?? new YouTubeQuotaBudgets(new YouTubeManualClock(Now)),
			options ?? ManualOptions());

	public static YouTubeAccount Account(string channelId = "UCchannel", string title = "Channel")
		=> new(Guid.NewGuid(), "client-1", channelId, title, null, "channel", Now);

	public static YouTubeBroadcast Broadcast(
		string id,
		string lifeCycleStatus,
		string? liveChatId = null,
		bool enableAutoStart = false,
		DateTimeOffset? scheduledStart = null,
		string title = "My stream",
		bool enableMonitorStream = false)
		=> new(id,
			title,
			liveChatId,
			lifeCycleStatus == YouTubeLifeCycleStatus.Live ? Now.AddMinutes(-30) : null,
			scheduledStart,
			lifeCycleStatus,
			enableAutoStart,
			enableMonitorStream);

	public static YouTubeVideo Video(
		string id,
		long? viewers = 42,
		long? likes = 7,
		string title = "My stream",
		string? liveChatId = "chat-1")
		=> new(id,
			new YouTubeVideoSnippet(title,
				"An old description",
				["old", "tags"],
				"20",
				"en",
				"en-US",
				$"https://i.ytimg.com/vi/{id}/maxresdefault_live.jpg"),
			likes,
			viewers,
			Now.AddMinutes(-30),
			liveChatId);

	public static YouTubeChatAuthor Author(string channelId = "UCviewer", string name = "Viewer")
		=> new(channelId, name, null, false, false, false, false);

	public static YouTubeChatMessage Text(string id, string text, string authorId = "UCviewer")
		=> new(id, YouTubeChatMessageTypes.Text, Now, text, Author(authorId)) { TextMessage = text };

	public static YouTubeApiException ApiError(string reason, HttpStatusCode status = HttpStatusCode.Forbidden)
		=> new(status, reason, $"YouTube answered {(int)status} {reason}");
}

internal sealed class FakeYouTubeOAuthClient : IYouTubeOAuthClient
{
	public YouTubeDeviceCode DeviceCode { get; set; } = new("device-1",
		"ABCD-EFGH",
		"https://www.google.com/device",
		TimeSpan.FromMinutes(30),
		TimeSpan.FromSeconds(5));

	public Queue<YouTubeTokenPoll> PollResults { get; } = new();

	public Exception? DeviceCodeFailure { get; set; }

	public List<string> Calls { get; } = [];

	public List<(string ClientId, string Secret)> PolledWith { get; } = [];

	public Task<YouTubeDeviceCode> RequestDeviceCodeAsync(string clientId, CancellationToken cancellationToken)
	{
		Calls.Add("device:" + clientId);
		if (DeviceCodeFailure is { } failure)
		{
			throw failure;
		}

		return Task.FromResult(DeviceCode);
	}

	public Task<YouTubeTokenPoll> PollTokenAsync(
		string clientId,
		string clientSecret,
		string deviceCode,
		CancellationToken cancellationToken)
	{
		Calls.Add("poll:" + deviceCode);
		PolledWith.Add((clientId, clientSecret));
		return Task.FromResult(PollResults.Count > 0
			? PollResults.Dequeue()
			: new YouTubeTokenPoll(YouTubeTokenPollStatus.Pending));
	}

	public Task<YouTubeTokens> RefreshAsync(
		string clientId,
		string clientSecret,
		string refreshToken,
		CancellationToken cancellationToken)
		=> Task.FromResult(new YouTubeTokens("refreshed", refreshToken, DateTimeOffset.UtcNow.AddHours(1),
			[YouTubeOAuthEndpoints.Scope]));

	public void Dispose()
	{
	}
}

internal sealed class FakeYouTubeApiClient : IYouTubeApiClient
{
	private readonly Lock _sync = new();

	public List<YouTubeBroadcast> Active { get; } = [];

	public List<YouTubeBroadcast> Upcoming { get; } = [];

	public Dictionary<string, YouTubeVideo> Videos { get; } = new(StringComparer.Ordinal);

	public YouTubeChannel? Channel { get; set; } = new("UCchannel", "Channel", "@channel", 1200);

	public Queue<YouTubeChatPage> ChatPages { get; } = new();

	public Action<string, string>? Transitioned { get; set; }

	public ConcurrentDictionary<string, Exception> Failures { get; } = new(StringComparer.Ordinal);

	private readonly List<string> _calls = [];

	public IReadOnlyList<string> Calls
	{
		get
		{
			lock (_sync)
			{
				return [.. _calls];
			}
		}
	}

	public List<string?> PageTokens { get; } = [];

	public List<(string LiveChatId, string Text)> SentMessages { get; } = [];

	public List<string> DeletedMessages { get; } = [];

	public List<(string LiveChatId, string ChannelId, long? Duration)> Bans { get; } = [];

	public List<string> DeletedBans { get; } = [];

	public List<(string VideoId, YouTubeVideoSnippet Snippet)> Updates { get; } = [];

	public List<(string BroadcastId, string Status)> Transitions { get; } = [];

	public List<(string BroadcastId, int Seconds)> Cuepoints { get; } = [];

	public string NextBanId { get; set; } = "ban-1";

	public Task<IReadOnlyList<YouTubeBroadcast>> ListBroadcastsAsync(
		string broadcastStatus,
		CancellationToken cancellationToken)
	{
		Record("broadcasts:" + broadcastStatus);
		return Task.FromResult<IReadOnlyList<YouTubeBroadcast>>(broadcastStatus == YouTubeBroadcastStatus.Active
			? [.. Active]
			: [.. Upcoming]);
	}

	public Task<YouTubeVideo?> GetVideoAsync(string videoId, CancellationToken cancellationToken)
	{
		Record("video:" + videoId);
		return Task.FromResult(Videos.GetValueOrDefault(videoId));
	}

	public Task<YouTubeChannel?> GetMyChannelAsync(CancellationToken cancellationToken)
	{
		Record("channel");
		return Task.FromResult(Channel);
	}

	public Task<YouTubeChatPage> ListChatMessagesAsync(
		string liveChatId,
		string? pageToken,
		CancellationToken cancellationToken)
	{
		Record("chat:" + liveChatId);
		lock (_sync)
		{
			PageTokens.Add(pageToken);
			return Task.FromResult(ChatPages.Count > 0
				? ChatPages.Dequeue()
				: new YouTubeChatPage(pageToken, 1000, null, []));
		}
	}

	public Task<string> InsertChatMessageAsync(string liveChatId, string text, CancellationToken cancellationToken)
	{
		Record("send");
		SentMessages.Add((liveChatId, text));
		return Task.FromResult("sent-1");
	}

	public Task DeleteChatMessageAsync(string messageId, CancellationToken cancellationToken)
	{
		Record("delete");
		DeletedMessages.Add(messageId);
		return Task.CompletedTask;
	}

	public Task<string> InsertBanAsync(
		string liveChatId,
		string channelId,
		long? durationSeconds,
		CancellationToken cancellationToken)
	{
		Record("ban");
		Bans.Add((liveChatId, channelId, durationSeconds));
		return Task.FromResult(NextBanId);
	}

	public Task DeleteBanAsync(string banId, CancellationToken cancellationToken)
	{
		Record("unban");
		DeletedBans.Add(banId);
		return Task.CompletedTask;
	}

	public Task UpdateVideoSnippetAsync(
		string videoId,
		YouTubeVideoSnippet snippet,
		CancellationToken cancellationToken)
	{
		Record("update");
		Updates.Add((videoId, snippet));
		return Task.CompletedTask;
	}

	public Task<string> TransitionBroadcastAsync(
		string broadcastId,
		string broadcastStatus,
		CancellationToken cancellationToken)
	{
		Record("transition");
		Transitions.Add((broadcastId, broadcastStatus));
		Transitioned?.Invoke(broadcastId, broadcastStatus);
		return Task.FromResult(broadcastStatus);
	}

	public Task InsertCuepointAsync(string broadcastId, int durationSeconds, CancellationToken cancellationToken)
	{
		Record("cuepoint");
		Cuepoints.Add((broadcastId, durationSeconds));
		return Task.CompletedTask;
	}

	private void Record(string call)
	{
		lock (_sync)
		{
			_calls.Add(call);
		}

		var name = call.Split(':')[0];
		if (Failures.TryGetValue(name, out var failure))
		{
			throw failure;
		}
	}
}

internal sealed class RecordingYouTubeChatSink : IStreamChatSink
{
	private readonly Lock _sync = new();
	private readonly List<ChatEvent> _posted = [];

	public List<IReadOnlyList<ChatAccount>> AccountLists { get; } = [];

	public IReadOnlyList<ChatEvent> Posted
	{
		get
		{
			lock (_sync)
			{
				return [.. _posted];
			}
		}
	}

	public void SetAccounts(IReadOnlyList<ChatAccount> accounts) => AccountLists.Add(accounts);

	public void Post(ChatEvent chatEvent)
	{
		lock (_sync)
		{
			_posted.Add(chatEvent);
		}
	}
}

internal sealed class RecordingYouTubeEventPublisher : IEventPublisher
{
	public List<(string EventId, IReadOnlyDictionary<string, object?> Payload)> Published { get; } = [];

	public IEnumerable<string> Ids => Published.Select(entry => entry.EventId);

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
		=> Published.Add((eventId, parameters ?? new Dictionary<string, object?>()));

	public IReadOnlyDictionary<string, object?> Single(string eventId)
		=> Published.Single(entry => entry.EventId == eventId).Payload;
}
