using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Serilog;
using MacroDeckHost.Integrations.Http;

namespace MacroDeckHost.Integrations.YouTube.Protocol;

internal sealed class YouTubeApiClient : IYouTubeApiClient, IDisposable
{
	internal const string BaseUrl = "https://www.googleapis.com/youtube/v3/";

	private const string BroadcastParts = "id,snippet,contentDetails,status";

	private static readonly HttpClient _shared = CreateClient(IntegrationHttp.CreateHandler(), disposeHandler: true);

	private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web)
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
	};

	private readonly HttpClient _http;
	private readonly bool _ownsClient;
	private readonly Func<CancellationToken, Task<string>> _accessToken;
	private readonly Func<CancellationToken, Task> _forceRefresh;
	private readonly YouTubeQuotaBudget _budget;
	private readonly ILogger _logger;

	public YouTubeApiClient(
		Func<CancellationToken, Task<string>> accessToken,
		Func<CancellationToken, Task> forceRefresh,
		YouTubeQuotaBudget budget,
		ILogger logger)
	{
		_http = _shared;
		_ownsClient = false;
		_accessToken = accessToken;
		_forceRefresh = forceRefresh;
		_budget = budget;
		_logger = logger;
	}

	internal YouTubeApiClient(
		HttpMessageHandler handler,
		Func<CancellationToken, Task<string>> accessToken,
		Func<CancellationToken, Task> forceRefresh,
		YouTubeQuotaBudget budget,
		ILogger logger)
	{
		_http = CreateClient(handler, disposeHandler: false);
		_ownsClient = true;
		_accessToken = accessToken;
		_forceRefresh = forceRefresh;
		_budget = budget;
		_logger = logger;
	}

	public async Task<IReadOnlyList<YouTubeBroadcast>> ListBroadcastsAsync(
		string broadcastStatus,
		CancellationToken cancellationToken)
	{
		// Without broadcastType=all the persistent "Stream now" broadcast is never listed.
		var response = await GetAsync<YouTubeListResponse<YouTubeBroadcastWire>>(
			$"liveBroadcasts?part={BroadcastParts}&broadcastStatus={Escape(broadcastStatus)}&broadcastType=all&maxResults=50",
			YouTubeQuotaCosts.List,
			cancellationToken);

		return
		[
			.. (response.Items ?? [])
				.Where(item => !string.IsNullOrEmpty(item.Id))
				.Select(item => new YouTubeBroadcast(item.Id!,
					item.Snippet?.Title ?? string.Empty,
					NullIfEmpty(item.Snippet?.LiveChatId),
					ParseTime(item.Snippet?.ActualStartTime),
					ParseTime(item.Snippet?.ScheduledStartTime),
					item.Status?.LifeCycleStatus ?? string.Empty,
					item.ContentDetails?.EnableAutoStart ?? false,
					item.ContentDetails?.MonitorStream?.EnableMonitorStream ?? false))
		];
	}

	public async Task<YouTubeVideo?> GetVideoAsync(string videoId, CancellationToken cancellationToken)
	{
		var response = await GetAsync<YouTubeListResponse<YouTubeVideoWire>>(
			$"videos?part=snippet,statistics,liveStreamingDetails&id={Escape(videoId)}",
			YouTubeQuotaCosts.List,
			cancellationToken);

		var item = response.Items?.FirstOrDefault(video => !string.IsNullOrEmpty(video.Id));
		if (item is null)
		{
			return null;
		}

		var snippet = item.Snippet;
		return new YouTubeVideo(item.Id!,
			new YouTubeVideoSnippet(snippet?.Title ?? string.Empty,
				snippet?.Description ?? string.Empty,
				snippet?.Tags ?? [],
				snippet?.CategoryId ?? string.Empty,
				NullIfEmpty(snippet?.DefaultLanguage),
				NullIfEmpty(snippet?.DefaultAudioLanguage),
				BestThumbnail(snippet?.Thumbnails)),
			item.Statistics?.LikeCount,
			item.LiveStreamingDetails?.ConcurrentViewers,
			ParseTime(item.LiveStreamingDetails?.ActualStartTime),
			NullIfEmpty(item.LiveStreamingDetails?.ActiveLiveChatId));
	}

	public async Task<YouTubeChannel?> GetMyChannelAsync(CancellationToken cancellationToken)
	{
		var response = await GetAsync<YouTubeListResponse<YouTubeChannelWire>>(
			"channels?part=snippet,statistics&mine=true",
			YouTubeQuotaCosts.List,
			cancellationToken);

		var item = response.Items?.FirstOrDefault(channel => !string.IsNullOrEmpty(channel.Id));
		if (item is null)
		{
			return null;
		}

		var statistics = item.Statistics;
		return new YouTubeChannel(item.Id!,
			item.Snippet?.Title ?? string.Empty,
			NullIfEmpty(item.Snippet?.CustomUrl),
			statistics is { HiddenSubscriberCount: not true } ? statistics.SubscriberCount : null);
	}

	public async Task<YouTubeChatPage> ListChatMessagesAsync(
		string liveChatId,
		string? pageToken,
		CancellationToken cancellationToken)
	{
		var url = $"liveChat/messages?liveChatId={Escape(liveChatId)}&part=id,snippet,authorDetails&maxResults=500";
		if (!string.IsNullOrEmpty(pageToken))
		{
			url += $"&pageToken={Escape(pageToken)}";
		}

		var response = await GetAsync<YouTubeListResponse<YouTubeChatMessageWire>>(url,
			YouTubeQuotaCosts.ChatList,
			cancellationToken);

		return new YouTubeChatPage(NullIfEmpty(response.NextPageToken),
			(int)Math.Clamp(response.PollingIntervalMillis ?? 0, 0, int.MaxValue),
			ParseTime(response.OfflineAt),
			[.. (response.Items ?? []).Where(item => !string.IsNullOrEmpty(item.Id)).Select(ToChatMessage)]);
	}

	public async Task<string> InsertChatMessageAsync(
		string liveChatId,
		string text,
		CancellationToken cancellationToken)
	{
		var body = new YouTubeChatMessageInsertWire(new YouTubeChatMessageInsertSnippetWire(liveChatId,
			YouTubeChatMessageTypes.Text,
			new YouTubeTextMessageInsertWire(text)));

		var response = await SendForJsonAsync<YouTubeIdWire>(HttpMethod.Post,
			"liveChat/messages?part=snippet",
			body,
			YouTubeQuotaCosts.Write,
			cancellationToken);

		return response.Id ?? string.Empty;
	}

	public Task DeleteChatMessageAsync(string messageId, CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Delete,
			$"liveChat/messages?id={Escape(messageId)}",
			null,
			YouTubeQuotaCosts.Write,
			cancellationToken);

	public async Task<string> InsertBanAsync(
		string liveChatId,
		string channelId,
		long? durationSeconds,
		CancellationToken cancellationToken)
	{
		var body = new YouTubeBanInsertWire(new YouTubeBanInsertSnippetWire(liveChatId,
			durationSeconds is null ? "permanent" : "temporary",
			durationSeconds,
			new YouTubeBannedUserInsertWire(channelId)));

		var response = await SendForJsonAsync<YouTubeIdWire>(HttpMethod.Post,
			"liveChat/bans?part=snippet",
			body,
			YouTubeQuotaCosts.Write,
			cancellationToken);

		return response.Id ?? string.Empty;
	}

	public Task DeleteBanAsync(string banId, CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Delete,
			$"liveChat/bans?id={Escape(banId)}",
			null,
			YouTubeQuotaCosts.Write,
			cancellationToken);

	public Task UpdateVideoSnippetAsync(
		string videoId,
		YouTubeVideoSnippet snippet,
		CancellationToken cancellationToken)
	{
		// videos.update overwrites the whole snippet, so every field the caller read is sent back.
		var body = new YouTubeVideoUpdateWire(videoId,
			new YouTubeVideoUpdateSnippetWire(snippet.Title,
				snippet.CategoryId,
				snippet.Description,
				snippet.Tags,
				NullIfEmpty(snippet.DefaultLanguage),
				NullIfEmpty(snippet.DefaultAudioLanguage)));

		return SendAsync(HttpMethod.Put, "videos?part=snippet", body, YouTubeQuotaCosts.Write, cancellationToken);
	}

	public async Task<string> TransitionBroadcastAsync(
		string broadcastId,
		string broadcastStatus,
		CancellationToken cancellationToken)
	{
		var response = await SendForJsonAsync<YouTubeIdWire>(HttpMethod.Post,
			$"liveBroadcasts/transition?broadcastStatus={Escape(broadcastStatus)}&id={Escape(broadcastId)}&part=id,status",
			null,
			YouTubeQuotaCosts.Write,
			cancellationToken);

		return response.Status?.LifeCycleStatus ?? string.Empty;
	}

	public Task InsertCuepointAsync(string broadcastId, int durationSeconds, CancellationToken cancellationToken)
		=> SendAsync(HttpMethod.Post,
			$"liveBroadcasts/cuepoint?id={Escape(broadcastId)}",
			new YouTubeCuepointWire("cueTypeAd", durationSeconds),
			YouTubeQuotaCosts.Write,
			cancellationToken);

	public void Dispose()
	{
		if (_ownsClient)
		{
			_http.Dispose();
		}
	}

	private Task<T> GetAsync<T>(string relativeUrl, int cost, CancellationToken cancellationToken)
		where T : class
		=> SendForJsonAsync<T>(HttpMethod.Get, relativeUrl, null, cost, cancellationToken);

	private async Task<T> SendForJsonAsync<T>(
		HttpMethod method,
		string relativeUrl,
		object? body,
		int cost,
		CancellationToken cancellationToken)
		where T : class
	{
		var text = await SendAsync(method, relativeUrl, body, cost, cancellationToken);

		if (string.IsNullOrWhiteSpace(text))
		{
			throw new YouTubeTransientException("YouTube returned an empty response.");
		}

		try
		{
			return JsonSerializer.Deserialize<T>(text, _json)
				?? throw new YouTubeTransientException("YouTube returned an empty response.");
		}
		catch (JsonException ex)
		{
			throw new YouTubeTransientException("YouTube returned a response that could not be read.", ex);
		}
	}

	private async Task<string> SendAsync(
		HttpMethod method,
		string relativeUrl,
		object? body,
		int cost,
		CancellationToken cancellationToken)
	{
		var payload = body is null ? null : JsonSerializer.Serialize(body, body.GetType(), _json);
		var refreshed = false;

		while (true)
		{
			var accessToken = await _accessToken(cancellationToken);

			// The content is owned by the request and disposed with it, so it must not be disposed here.
			using var request = new HttpRequestMessage(method, BaseUrl + relativeUrl);
			request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
			if (payload is not null)
			{
				request.Content = new StringContent(payload, Encoding.UTF8, "application/json");
			}

			_budget.Charge(cost);

			using var response = await TransmitAsync(request, cancellationToken);
			var text = await ReadBodyAsync(response, cancellationToken);

			if (response.IsSuccessStatusCode)
			{
				return text;
			}

			if (response.StatusCode is HttpStatusCode.Unauthorized && !refreshed)
			{
				refreshed = true;
				_logger.Debug("YouTube refused the access token; refreshing it once and retrying");
				await _forceRefresh(cancellationToken);
				continue;
			}

			if ((int)response.StatusCode >= 500)
			{
				throw new YouTubeTransientException($"YouTube answered {(int)response.StatusCode}.");
			}

			var error = YouTubeApiException.FromResponse(response.StatusCode, text);
			if (error.IsQuotaExceeded)
			{
				_budget.MarkExhausted();
				_logger.Warning("YouTube reports the daily quota as exhausted until {ResetsAt}", _budget.ResetsAt);
			}

			throw error;
		}
	}

	private async Task<HttpResponseMessage> TransmitAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		try
		{
			return await _http.SendAsync(request, cancellationToken);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw new YouTubeTransientException("The request to YouTube timed out.");
		}
		catch (HttpRequestException ex)
		{
			throw new YouTubeTransientException("YouTube could not be reached.", ex);
		}
	}

	private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
	{
		try
		{
			return await response.Content.ReadAsStringAsync(cancellationToken);
		}
		catch (HttpRequestException ex)
		{
			throw new YouTubeTransientException("The response from YouTube could not be read.", ex);
		}
	}

	private static YouTubeChatMessage ToChatMessage(YouTubeChatMessageWire item)
	{
		var snippet = item.Snippet;
		var author = item.AuthorDetails;
		var superChat = snippet?.SuperChatDetails;
		var superSticker = snippet?.SuperStickerDetails;
		var milestone = snippet?.MemberMilestoneChatDetails;
		var sponsor = snippet?.NewSponsorDetails;
		var gifting = snippet?.MembershipGiftingDetails;
		var banned = snippet?.UserBannedDetails;

		return new YouTubeChatMessage(item.Id!,
			snippet?.Type ?? string.Empty,
			ParseTime(snippet?.PublishedAt),
			snippet?.DisplayMessage,
			new YouTubeChatAuthor(author?.ChannelId ?? snippet?.AuthorChannelId ?? string.Empty,
				author?.DisplayName ?? string.Empty,
				NullIfEmpty(author?.ProfileImageUrl),
				author?.IsChatOwner ?? false,
				author?.IsChatModerator ?? false,
				author?.IsChatSponsor ?? false,
				author?.IsVerified ?? false))
		{
			TextMessage = snippet?.TextMessageDetails?.MessageText,
			SuperChat = superChat is null
				? null
				: new YouTubeSuperChat(superChat.AmountMicros ?? 0,
					superChat.Currency ?? string.Empty,
					superChat.AmountDisplayString ?? string.Empty,
					NullIfEmpty(superChat.UserComment),
					superChat.Tier ?? 0),
			SuperSticker = superSticker is null
				? null
				: new YouTubeSuperSticker(superSticker.AmountMicros ?? 0,
					superSticker.Currency ?? string.Empty,
					superSticker.AmountDisplayString ?? string.Empty,
					superSticker.Tier ?? 0,
					NullIfEmpty(superSticker.SuperStickerMetadata?.StickerId),
					NullIfEmpty(superSticker.SuperStickerMetadata?.AltText)),
			MemberMilestone = milestone is null
				? null
				: new YouTubeMemberMilestone(NullIfEmpty(milestone.UserComment),
					milestone.MemberMonth,
					NullIfEmpty(milestone.MemberLevelName)),
			NewSponsor = sponsor is null
				? null
				: new YouTubeNewSponsor(NullIfEmpty(sponsor.MemberLevelName), sponsor.IsUpgrade ?? false),
			MembershipGifting = gifting is null
				? null
				: new YouTubeMembershipGifting(gifting.GiftMembershipsCount ?? 0,
					NullIfEmpty(gifting.GiftMembershipsLevelName)),
			UserBanned = banned is null
				? null
				: new YouTubeUserBanned(banned.BannedUserDetails?.ChannelId ?? string.Empty,
					NullIfEmpty(banned.BannedUserDetails?.DisplayName),
					banned.BanType ?? string.Empty,
					banned.BanDurationSeconds),
			DeletedMessageId = NullIfEmpty(snippet?.MessageDeletedDetails?.DeletedMessageId)
				?? NullIfEmpty(snippet?.MessageRetractedDetails?.RetractedMessageId)
		};
	}

	private static string? BestThumbnail(Dictionary<string, YouTubeThumbnailWire>? thumbnails)
	{
		if (thumbnails is null)
		{
			return null;
		}

		foreach (var size in (string[])["maxres", "standard", "high", "medium", "default"])
		{
			if (thumbnails.TryGetValue(size, out var thumbnail) && !string.IsNullOrEmpty(thumbnail.Url))
			{
				return thumbnail.Url;
			}
		}

		return null;
	}

	private static DateTimeOffset? ParseTime(string? value)
		=> DateTimeOffset.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
			out var parsed)
			? parsed
			: null;

	private static string? NullIfEmpty(string? value) => string.IsNullOrEmpty(value) ? null : value;

	private static string Escape(string value) => Uri.EscapeDataString(value);

	private static HttpClient CreateClient(HttpMessageHandler handler, bool disposeHandler)
		=> new(handler, disposeHandler) { Timeout = TimeSpan.FromSeconds(15) };
}
