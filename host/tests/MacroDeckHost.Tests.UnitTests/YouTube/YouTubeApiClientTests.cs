using System.Net;
using System.Text.Json;
using MacroDeckHost.Integrations.YouTube.Protocol;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeApiClientTests
{
	private const string QuotaExceededBody =
		"""
		{
		  "error": {
		    "code": 403,
		    "message": "The request cannot be completed because you have exceeded your quota.",
		    "errors": [
		      {
		        "message": "The request cannot be completed because you have exceeded your quota.",
		        "domain": "youtube.quota",
		        "reason": "quotaExceeded"
		      }
		    ],
		    "status": "PERMISSION_DENIED"
		  }
		}
		""";

	private YouTubeFakeHttpHandler _handler = null!;
	private YouTubeQuotaBudget _budget = null!;
	private YouTubeApiClient _client = null!;
	private int _refreshes;

	[SetUp]
	public void SetUp()
	{
		_handler = new YouTubeFakeHttpHandler();
		_budget = new YouTubeQuotaBudget(10_000,
			new YouTubeManualClock(new DateTimeOffset(2026, 10, 5, 19, 0, 0, TimeSpan.Zero)));
		_refreshes = 0;
		_client = new YouTubeApiClient(_handler,
			_ => Task.FromResult($"token-{_refreshes}"),
			_ =>
			{
				_refreshes++;
				return Task.CompletedTask;
			},
			_budget,
			new LoggerConfiguration().CreateLogger());
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_handler.Dispose();
	}

	[TestCase(YouTubeBroadcastStatus.Active)]
	[TestCase(YouTubeBroadcastStatus.Upcoming)]
	public async Task Every_broadcast_listing_includes_persistent_broadcasts(string status)
	{
		_handler.Enqueue(HttpStatusCode.OK, """{"kind":"youtube#liveBroadcastListResponse","items":[]}""");

		await _client.ListBroadcastsAsync(status, CancellationToken.None);

		var query = Query(_handler.RequestedUris[0]);
		Assert.Multiple(() =>
		{
			Assert.That(_handler.RequestedUris[0].AbsolutePath, Is.EqualTo("/youtube/v3/liveBroadcasts"));
			Assert.That(query["broadcastType"], Is.EqualTo("all"));
			Assert.That(query["broadcastStatus"], Is.EqualTo(status));
			Assert.That(query["part"], Is.EqualTo("id,snippet,contentDetails,status"));
			Assert.That(_handler.AuthorizationHeaders[0], Is.EqualTo("Bearer token-0"));
			Assert.That(_budget.Spent, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_live_persistent_broadcast_parses()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""
			{
			  "kind": "youtube#liveBroadcastListResponse",
			  "etag": "e1",
			  "pageInfo": { "totalResults": 1, "resultsPerPage": 50 },
			  "items": [
			    {
			      "kind": "youtube#liveBroadcast",
			      "etag": "e2",
			      "id": "dQw4w9WgXcQ",
			      "snippet": {
			        "publishedAt": "2026-09-01T10:00:00Z",
			        "channelId": "UCabc",
			        "title": "Late night stream",
			        "description": "",
			        "thumbnails": { "default": { "url": "https://i.ytimg.com/vi/dQw4w9WgXcQ/default_live.jpg", "width": 120, "height": 90 } },
			        "scheduledStartTime": "1970-01-01T00:00:00Z",
			        "actualStartTime": "2026-10-05T18:30:12.345Z",
			        "isDefaultBroadcast": true,
			        "liveChatId": "KicKGFVDYWJj"
			      },
			      "status": {
			        "lifeCycleStatus": "live",
			        "privacyStatus": "public",
			        "recordingStatus": "recording",
			        "madeForKids": false,
			        "selfDeclaredMadeForKids": false
			      },
			      "contentDetails": {
			        "boundStreamId": "abcStream",
			        "boundStreamLastUpdateTimeMs": "2026-10-05T18:30:00Z",
			        "monitorStream": { "enableMonitorStream": false, "broadcastStreamDelayMs": 0 },
			        "enableEmbed": true,
			        "enableDvr": true,
			        "recordFromStart": true,
			        "enableClosedCaptions": false,
			        "closedCaptionsType": "closedCaptionsDisabled",
			        "enableLowLatency": false,
			        "latencyPreference": "ultraLow",
			        "projection": "rectangular",
			        "enableAutoStart": true,
			        "enableAutoStop": true
			      }
			    }
			  ]
			}
			""");

		var broadcasts = await _client.ListBroadcastsAsync(YouTubeBroadcastStatus.Active, CancellationToken.None);

		Assert.That(broadcasts, Has.Count.EqualTo(1));
		var broadcast = broadcasts[0];
		Assert.Multiple(() =>
		{
			Assert.That(broadcast.Id, Is.EqualTo("dQw4w9WgXcQ"));
			Assert.That(broadcast.Title, Is.EqualTo("Late night stream"));
			Assert.That(broadcast.LifeCycleStatus, Is.EqualTo(YouTubeLifeCycleStatus.Live));
			Assert.That(broadcast.LiveChatId, Is.EqualTo("KicKGFVDYWJj"));
			Assert.That(broadcast.ActualStartTime,
				Is.EqualTo(new DateTimeOffset(2026, 10, 5, 18, 30, 12, 345, TimeSpan.Zero)));
			Assert.That(broadcast.EnableAutoStart, Is.True);
			Assert.That(broadcast.EnableMonitorStream, Is.False);
		});
	}

	[Test]
	public async Task A_live_video_reads_counts_sent_as_strings()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""
			{
			  "kind": "youtube#videoListResponse",
			  "items": [
			    {
			      "kind": "youtube#video",
			      "id": "dQw4w9WgXcQ",
			      "snippet": {
			        "title": "Late night stream",
			        "description": "Chill",
			        "tags": ["chill", "lofi"],
			        "categoryId": "20",
			        "defaultAudioLanguage": "en",
			        "liveBroadcastContent": "live",
			        "thumbnails": {
			          "default": { "url": "https://i.ytimg.com/d.jpg" },
			          "medium": { "url": "https://i.ytimg.com/m.jpg" },
			          "high": { "url": "https://i.ytimg.com/h.jpg" }
			        }
			      },
			      "statistics": { "viewCount": "1200", "likeCount": "9007199254740993", "favoriteCount": "0", "commentCount": "0" },
			      "liveStreamingDetails": {
			        "actualStartTime": "2026-10-05T18:30:12Z",
			        "scheduledStartTime": "2026-10-05T18:30:00Z",
			        "concurrentViewers": "42",
			        "activeLiveChatId": "KicKGFVDYWJj"
			      }
			    }
			  ]
			}
			""");

		var video = await _client.GetVideoAsync("dQw4w9WgXcQ", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Query(_handler.RequestedUris[0])["part"], Is.EqualTo("snippet,statistics,liveStreamingDetails"));
			Assert.That(video!.LikeCount, Is.EqualTo(9007199254740993L));
			Assert.That(video.ConcurrentViewers, Is.EqualTo(42));
			Assert.That(video.ActiveLiveChatId, Is.EqualTo("KicKGFVDYWJj"));
			Assert.That(video.Snippet.Tags, Is.EqualTo(new[] { "chill", "lofi" }));
			Assert.That(video.Snippet.CategoryId, Is.EqualTo("20"));
			Assert.That(video.Snippet.DefaultLanguage, Is.Null);
			Assert.That(video.Snippet.DefaultAudioLanguage, Is.EqualTo("en"));
			Assert.That(video.Snippet.ThumbnailUrl, Is.EqualTo("https://i.ytimg.com/h.jpg"));
		});
	}

	[Test]
	public async Task A_video_without_concurrent_viewers_reports_none()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""
			{"items":[{"id":"v1","snippet":{"title":"t","categoryId":"22"},
			  "statistics":{"viewCount":"3"},
			  "liveStreamingDetails":{"actualStartTime":"2026-10-05T18:30:12Z","actualEndTime":"2026-10-05T19:30:12Z"}}]}
			""");

		var video = await _client.GetVideoAsync("v1", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(video!.ConcurrentViewers, Is.Null);
			Assert.That(video.LikeCount, Is.Null);
			Assert.That(video.Snippet.Tags, Is.Empty);
		});
	}

	[Test]
	public async Task An_unknown_video_is_null()
	{
		_handler.Enqueue(HttpStatusCode.OK, """{"kind":"youtube#videoListResponse","items":[]}""");

		Assert.That(await _client.GetVideoAsync("gone", CancellationToken.None), Is.Null);
	}

	[Test]
	public async Task A_channel_with_a_hidden_subscriber_count_reports_none()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""
			{"items":[{"kind":"youtube#channel","id":"UCabc",
			  "snippet":{"title":"Streamer","customUrl":"@streamer"},
			  "statistics":{"viewCount":"1000","subscriberCount":"0","hiddenSubscriberCount":true,"videoCount":"5"}}]}
			""");

		var channel = await _client.GetMyChannelAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Query(_handler.RequestedUris[0])["mine"], Is.EqualTo("true"));
			Assert.That(channel!.Id, Is.EqualTo("UCabc"));
			Assert.That(channel.Handle, Is.EqualTo("@streamer"));
			Assert.That(channel.SubscriberCount, Is.Null);
		});
	}

	[Test]
	public async Task A_channel_with_a_public_subscriber_count_reports_it()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""
			{"items":[{"id":"UCabc","snippet":{"title":"Streamer"},
			  "statistics":{"subscriberCount":"1234","hiddenSubscriberCount":false}}]}
			""");

		var channel = await _client.GetMyChannelAsync(CancellationToken.None);

		Assert.That(channel!.SubscriberCount, Is.EqualTo(1234));
	}

	[Test]
	public void A_quota_exceeded_answer_is_reported_and_pauses_the_budget()
	{
		_handler.Enqueue(HttpStatusCode.Forbidden, QuotaExceededBody);

		var error = Assert.ThrowsAsync<YouTubeApiException>(() =>
			_client.ListBroadcastsAsync(YouTubeBroadcastStatus.Active, CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(error!.Status, Is.EqualTo(HttpStatusCode.Forbidden));
			Assert.That(error.Reason, Is.EqualTo("quotaExceeded"));
			Assert.That(error.IsQuotaExceeded, Is.True);
			Assert.That(_budget.IsExhausted, Is.True);
			Assert.That(_budget.Level, Is.EqualTo(YouTubeQuotaLevel.Paused));
		});
	}

	[Test]
	public void A_non_json_error_keeps_only_the_status()
	{
		var error = YouTubeApiException.FromResponse(HttpStatusCode.NotFound, "<html>Not Found</html>");

		Assert.Multiple(() =>
		{
			Assert.That(error.Status, Is.EqualTo(HttpStatusCode.NotFound));
			Assert.That(error.Reason, Is.Null);
			Assert.That(error.IsQuotaExceeded, Is.False);
		});
	}

	[TestCase("rateLimitExceeded")]
	[TestCase("userRequestsExceedRateLimit")]
	public void A_rate_limit_is_not_an_exhausted_quota(string reason)
	{
		_handler.Enqueue(HttpStatusCode.Forbidden,
			$$$"""{"error":{"code":403,"message":"slow","errors":[{"domain":"youtube.api","reason":"{{{reason}}}"}]}}""");

		var error = Assert.ThrowsAsync<YouTubeApiException>(() =>
			_client.ListChatMessagesAsync("chat", null, CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(error!.IsRateLimited, Is.True);
			Assert.That(error.IsQuotaExceeded, Is.False);
			Assert.That(_budget.IsExhausted, Is.False);
		});
	}

	[Test]
	public async Task A_refused_token_is_refreshed_once_and_the_call_retried()
	{
		_handler.Enqueue(HttpStatusCode.Unauthorized,
				"""{"error":{"code":401,"message":"Request had invalid authentication credentials.","status":"UNAUTHENTICATED"}}""")
			.Enqueue(HttpStatusCode.OK, """{"items":[]}""");

		await _client.ListBroadcastsAsync(YouTubeBroadcastStatus.Active, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_refreshes, Is.EqualTo(1));
			Assert.That(_handler.AuthorizationHeaders, Is.EqualTo(new[] { "Bearer token-0", "Bearer token-1" }));
		});
	}

	[Test]
	public void A_token_refused_after_the_refresh_is_not_retried_again()
	{
		const string unauthorized = """{"error":{"code":401,"message":"Invalid Credentials","status":"UNAUTHENTICATED"}}""";
		_handler.Enqueue(HttpStatusCode.Unauthorized, unauthorized).Enqueue(HttpStatusCode.Unauthorized, unauthorized);

		var error = Assert.ThrowsAsync<YouTubeApiException>(() =>
			_client.ListBroadcastsAsync(YouTubeBroadcastStatus.Active, CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(error!.IsUnauthorized, Is.True);
			Assert.That(_refreshes, Is.EqualTo(1));
			Assert.That(_handler.RequestedUris, Has.Count.EqualTo(2));
		});
	}

	[TestCase(HttpStatusCode.InternalServerError)]
	[TestCase(HttpStatusCode.ServiceUnavailable)]
	public void A_server_error_is_transient(HttpStatusCode status)
	{
		_handler.Enqueue(status, string.Empty);

		Assert.ThrowsAsync<YouTubeTransientException>(() =>
			_client.GetVideoAsync("v1", CancellationToken.None));
	}

	[Test]
	public void An_unreachable_api_is_transient()
	{
		_handler.EnqueueFailure(new HttpRequestException("no route"));

		Assert.ThrowsAsync<YouTubeTransientException>(() =>
			_client.GetMyChannelAsync(CancellationToken.None));
	}

	[Test]
	public async Task A_chat_page_parses_every_documented_message_kind()
	{
		_handler.Enqueue(HttpStatusCode.OK, ChatPageBody);

		var page = await _client.ListChatMessagesAsync("KicKGFVDYWJj", "token-a", CancellationToken.None);

		var query = Query(_handler.RequestedUris[0]);
		Assert.Multiple(() =>
		{
			Assert.That(_handler.RequestedUris[0].AbsolutePath, Is.EqualTo("/youtube/v3/liveChat/messages"));
			Assert.That(query["liveChatId"], Is.EqualTo("KicKGFVDYWJj"));
			Assert.That(query["part"], Is.EqualTo("id,snippet,authorDetails"));
			Assert.That(query["pageToken"], Is.EqualTo("token-a"));
			Assert.That(page.NextPageToken, Is.EqualTo("GLDPmLKNmY8DIJaZ"));
			Assert.That(page.PollingIntervalMillis, Is.EqualTo(5120));
			Assert.That(page.OfflineAt, Is.Null);
			Assert.That(page.Items, Has.Count.EqualTo(10));
		});

		var byId = page.Items.ToDictionary(item => item.Id);

		var text = byId["text"];
		Assert.Multiple(() =>
		{
			Assert.That(text.Type, Is.EqualTo(YouTubeChatMessageTypes.Text));
			Assert.That(text.TextMessage, Is.EqualTo("hello"));
			Assert.That(text.DisplayMessage, Is.EqualTo("hello"));
			Assert.That(text.PublishedAt, Is.EqualTo(new DateTimeOffset(2026, 10, 5, 19, 0, 1, TimeSpan.Zero)));
			Assert.That(text.Author.ChannelId, Is.EqualTo("UCviewer"));
			Assert.That(text.Author.DisplayName, Is.EqualTo("Viewer"));
			Assert.That(text.Author.ProfileImageUrl, Is.EqualTo("https://yt3.ggpht.com/viewer.jpg"));
			Assert.That(text.Author.IsChatModerator, Is.True);
			Assert.That(text.Author.IsChatOwner, Is.False);
			Assert.That(text.Author.IsChatSponsor, Is.True);
			Assert.That(text.Author.IsVerified, Is.False);
		});

		var superChat = byId["superchat"].SuperChat!;
		Assert.Multiple(() =>
		{
			Assert.That(superChat.AmountMicros, Is.EqualTo(5_000_000L));
			Assert.That(superChat.Currency, Is.EqualTo("USD"));
			Assert.That(superChat.AmountDisplayString, Is.EqualTo("$5.00"));
			Assert.That(superChat.UserComment, Is.EqualTo("great stream"));
			Assert.That(superChat.Tier, Is.EqualTo(2));
		});

		var sticker = byId["sticker"].SuperSticker!;
		Assert.Multiple(() =>
		{
			Assert.That(sticker.AmountMicros, Is.EqualTo(2_000_000L));
			Assert.That(sticker.StickerId, Is.EqualTo("sticker-1"));
			Assert.That(sticker.AltText, Is.EqualTo("Waving cat"));
			Assert.That(sticker.Tier, Is.EqualTo(1));
		});

		var milestone = byId["milestone"].MemberMilestone!;
		Assert.Multiple(() =>
		{
			Assert.That(milestone.MemberMonth, Is.EqualTo(6));
			Assert.That(milestone.MemberLevelName, Is.EqualTo("Gold"));
			Assert.That(milestone.UserComment, Is.EqualTo("half a year"));
		});

		Assert.Multiple(() =>
		{
			Assert.That(byId["sponsor"].NewSponsor, Is.EqualTo(new YouTubeNewSponsor("Gold", true)));
			Assert.That(byId["gifting"].MembershipGifting, Is.EqualTo(new YouTubeMembershipGifting(5, "Gold")));
			Assert.That(byId["banned"].UserBanned,
				Is.EqualTo(new YouTubeUserBanned("UCtroll", "Troll", "temporary", 300)));
			Assert.That(byId["deleted"].DeletedMessageId, Is.EqualTo("text-old"));
			Assert.That(byId["retracted"].DeletedMessageId, Is.EqualTo("text-older"));
			Assert.That(byId["unknown"].Type, Is.EqualTo("someFutureEvent"));
			Assert.That(byId["unknown"].TextMessage, Is.Null);
		});
	}

	[Test]
	public async Task A_chat_page_reports_when_the_chat_went_offline()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""{"pollingIntervalMillis":10000,"offlineAt":"2026-10-05T21:00:00Z","items":[]}""");

		var page = await _client.ListChatMessagesAsync("chat", null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(Query(_handler.RequestedUris[0]).ContainsKey("pageToken"), Is.False);
			Assert.That(page.OfflineAt, Is.EqualTo(new DateTimeOffset(2026, 10, 5, 21, 0, 0, TimeSpan.Zero)));
			Assert.That(page.NextPageToken, Is.Null);
		});
	}

	[Test]
	public async Task Sending_a_chat_message_posts_a_text_message_event()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""{"kind":"youtube#liveChatMessage","id":"msg-1","snippet":{"type":"textMessageEvent"}}""");

		var id = await _client.InsertChatMessageAsync("chat-1", "hi there", CancellationToken.None);

		using var body = JsonDocument.Parse(_handler.RequestBodies[0]);
		var snippet = body.RootElement.GetProperty("snippet");
		Assert.Multiple(() =>
		{
			Assert.That(id, Is.EqualTo("msg-1"));
			Assert.That(_handler.Methods[0], Is.EqualTo(HttpMethod.Post));
			Assert.That(Query(_handler.RequestedUris[0])["part"], Is.EqualTo("snippet"));
			Assert.That(snippet.GetProperty("liveChatId").GetString(), Is.EqualTo("chat-1"));
			Assert.That(snippet.GetProperty("type").GetString(), Is.EqualTo("textMessageEvent"));
			Assert.That(snippet.GetProperty("textMessageDetails").GetProperty("messageText").GetString(),
				Is.EqualTo("hi there"));
			Assert.That(_budget.Spent, Is.EqualTo(YouTubeQuotaCosts.Write));
		});
	}

	[Test]
	public async Task A_temporary_ban_carries_its_duration()
	{
		_handler.Enqueue(HttpStatusCode.OK, """{"kind":"youtube#liveChatBan","id":"ban-1"}""");

		var id = await _client.InsertBanAsync("chat-1", "UCtroll", 300, CancellationToken.None);

		using var body = JsonDocument.Parse(_handler.RequestBodies[0]);
		var snippet = body.RootElement.GetProperty("snippet");
		Assert.Multiple(() =>
		{
			Assert.That(id, Is.EqualTo("ban-1"));
			Assert.That(_handler.RequestedUris[0].AbsolutePath, Is.EqualTo("/youtube/v3/liveChat/bans"));
			Assert.That(snippet.GetProperty("type").GetString(), Is.EqualTo("temporary"));
			Assert.That(snippet.GetProperty("banDurationSeconds").GetInt64(), Is.EqualTo(300));
			Assert.That(snippet.GetProperty("liveChatId").GetString(), Is.EqualTo("chat-1"));
			Assert.That(snippet.GetProperty("bannedUserDetails").GetProperty("channelId").GetString(),
				Is.EqualTo("UCtroll"));
		});
	}

	[Test]
	public async Task A_permanent_ban_has_no_duration()
	{
		_handler.Enqueue(HttpStatusCode.OK, """{"id":"ban-2"}""");

		await _client.InsertBanAsync("chat-1", "UCtroll", null, CancellationToken.None);

		using var body = JsonDocument.Parse(_handler.RequestBodies[0]);
		var snippet = body.RootElement.GetProperty("snippet");
		Assert.Multiple(() =>
		{
			Assert.That(snippet.GetProperty("type").GetString(), Is.EqualTo("permanent"));
			Assert.That(snippet.TryGetProperty("banDurationSeconds", out _), Is.False);
		});
	}

	[Test]
	public async Task Deletes_address_the_message_or_ban_by_id()
	{
		_handler.Enqueue(HttpStatusCode.NoContent, string.Empty).Enqueue(HttpStatusCode.NoContent, string.Empty);

		await _client.DeleteChatMessageAsync("msg/1", CancellationToken.None);
		await _client.DeleteBanAsync("ban-1", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_handler.Methods, Is.All.EqualTo(HttpMethod.Delete));
			Assert.That(_handler.RequestedUris[0].AbsolutePath, Is.EqualTo("/youtube/v3/liveChat/messages"));
			Assert.That(Query(_handler.RequestedUris[0])["id"], Is.EqualTo("msg/1"));
			Assert.That(_handler.RequestedUris[1].AbsolutePath, Is.EqualTo("/youtube/v3/liveChat/bans"));
			Assert.That(Query(_handler.RequestedUris[1])["id"], Is.EqualTo("ban-1"));
			Assert.That(_budget.Spent, Is.EqualTo(2 * YouTubeQuotaCosts.Write));
		});
	}

	[Test]
	public async Task A_snippet_update_sends_back_category_and_tags()
	{
		_handler.Enqueue(HttpStatusCode.OK, """{"id":"v1"}""");

		await _client.UpdateVideoSnippetAsync("v1",
			new YouTubeVideoSnippet("New title", "Chill", ["chill", "lofi"], "20", DefaultAudioLanguage: "en"),
			CancellationToken.None);

		using var body = JsonDocument.Parse(_handler.RequestBodies[0]);
		var snippet = body.RootElement.GetProperty("snippet");
		Assert.Multiple(() =>
		{
			Assert.That(_handler.Methods[0], Is.EqualTo(HttpMethod.Put));
			Assert.That(Query(_handler.RequestedUris[0])["part"], Is.EqualTo("snippet"));
			Assert.That(body.RootElement.GetProperty("id").GetString(), Is.EqualTo("v1"));
			Assert.That(snippet.GetProperty("title").GetString(), Is.EqualTo("New title"));
			Assert.That(snippet.GetProperty("categoryId").GetString(), Is.EqualTo("20"));
			Assert.That(snippet.GetProperty("description").GetString(), Is.EqualTo("Chill"));
			Assert.That(snippet.GetProperty("tags").EnumerateArray().Select(tag => tag.GetString()),
				Is.EqualTo(new[] { "chill", "lofi" }));
			Assert.That(snippet.GetProperty("defaultAudioLanguage").GetString(), Is.EqualTo("en"));
			Assert.That(snippet.TryGetProperty("defaultLanguage", out _), Is.False);
			Assert.That(snippet.TryGetProperty("thumbnails", out _), Is.False);
		});
	}

	[Test]
	public async Task A_transition_reports_the_new_life_cycle_status()
	{
		_handler.Enqueue(HttpStatusCode.OK,
			"""{"kind":"youtube#liveBroadcast","id":"b1","status":{"lifeCycleStatus":"liveStarting","privacyStatus":"public"}}""");

		var status = await _client.TransitionBroadcastAsync("b1", YouTubeBroadcastTransition.Live, CancellationToken.None);

		var query = Query(_handler.RequestedUris[0]);
		Assert.Multiple(() =>
		{
			Assert.That(status, Is.EqualTo(YouTubeLifeCycleStatus.LiveStarting));
			Assert.That(_handler.RequestedUris[0].AbsolutePath, Is.EqualTo("/youtube/v3/liveBroadcasts/transition"));
			Assert.That(query["broadcastStatus"], Is.EqualTo("live"));
			Assert.That(query["id"], Is.EqualTo("b1"));
			Assert.That(_budget.Spent, Is.EqualTo(YouTubeQuotaCosts.Write));
		});
	}

	[Test]
	public async Task An_ad_break_inserts_an_ad_cuepoint()
	{
		_handler.Enqueue(HttpStatusCode.OK, """{"id":"cue-1","cueType":"cueTypeAd","durationSecs":60}""");

		await _client.InsertCuepointAsync("b1", 60, CancellationToken.None);

		using var body = JsonDocument.Parse(_handler.RequestBodies[0]);
		Assert.Multiple(() =>
		{
			Assert.That(_handler.RequestedUris[0].AbsolutePath, Is.EqualTo("/youtube/v3/liveBroadcasts/cuepoint"));
			Assert.That(Query(_handler.RequestedUris[0])["id"], Is.EqualTo("b1"));
			Assert.That(body.RootElement.GetProperty("cueType").GetString(), Is.EqualTo("cueTypeAd"));
			Assert.That(body.RootElement.GetProperty("durationSecs").GetInt32(), Is.EqualTo(60));
		});
	}

	[Test]
	public async Task Each_call_charges_its_documented_cost()
	{
		await _client.ListBroadcastsAsync(YouTubeBroadcastStatus.Active, CancellationToken.None);
		Assert.That(_budget.Spent, Is.EqualTo(1));

		await _client.GetVideoAsync("v1", CancellationToken.None);
		Assert.That(_budget.Spent, Is.EqualTo(2));

		await _client.GetMyChannelAsync(CancellationToken.None);
		Assert.That(_budget.Spent, Is.EqualTo(3));

		await _client.ListChatMessagesAsync("chat", null, CancellationToken.None);
		Assert.That(_budget.Spent, Is.EqualTo(4));

		_handler.Enqueue(HttpStatusCode.OK, """{"id":"msg"}""");
		await _client.InsertChatMessageAsync("chat", "hi", CancellationToken.None);
		Assert.That(_budget.Spent, Is.EqualTo(54));
	}

	private static Dictionary<string, string> Query(Uri uri)
		=> uri.Query.TrimStart('?')
			.Split('&', StringSplitOptions.RemoveEmptyEntries)
			.Select(pair => pair.Split('=', 2))
			.ToDictionary(pair => Uri.UnescapeDataString(pair[0]),
				pair => Uri.UnescapeDataString(pair.Length > 1 ? pair[1] : string.Empty),
				StringComparer.Ordinal);

	private const string ChatPageBody =
		"""
		{
		  "kind": "youtube#liveChatMessageListResponse",
		  "etag": "e",
		  "nextPageToken": "GLDPmLKNmY8DIJaZ",
		  "pollingIntervalMillis": 5120,
		  "pageInfo": { "totalResults": 10, "resultsPerPage": 10 },
		  "items": [
		    {
		      "kind": "youtube#liveChatMessage", "etag": "e", "id": "text",
		      "snippet": {
		        "type": "textMessageEvent", "liveChatId": "KicKGFVDYWJj", "authorChannelId": "UCviewer",
		        "publishedAt": "2026-10-05T19:00:01.000000Z", "hasDisplayContent": true, "displayMessage": "hello",
		        "textMessageDetails": { "messageText": "hello" }
		      },
		      "authorDetails": {
		        "channelId": "UCviewer", "channelUrl": "http://www.youtube.com/channel/UCviewer", "displayName": "Viewer",
		        "profileImageUrl": "https://yt3.ggpht.com/viewer.jpg", "isVerified": false, "isChatOwner": false,
		        "isChatSponsor": true, "isChatModerator": true
		      }
		    },
		    {
		      "id": "superchat",
		      "snippet": {
		        "type": "superChatEvent", "authorChannelId": "UCfan", "publishedAt": "2026-10-05T19:00:02Z",
		        "hasDisplayContent": true, "displayMessage": "$5.00 from Fan: \"great stream\"",
		        "superChatDetails": { "amountMicros": "5000000", "currency": "USD", "amountDisplayString": "$5.00",
		          "userComment": "great stream", "tier": 2 }
		      },
		      "authorDetails": { "channelId": "UCfan", "displayName": "Fan", "isVerified": false, "isChatOwner": false,
		        "isChatSponsor": false, "isChatModerator": false }
		    },
		    {
		      "id": "sticker",
		      "snippet": {
		        "type": "superStickerEvent", "authorChannelId": "UCfan", "publishedAt": "2026-10-05T19:00:03Z",
		        "hasDisplayContent": true, "displayMessage": "Fan sent a sticker",
		        "superStickerDetails": {
		          "superStickerMetadata": { "stickerId": "sticker-1", "altText": "Waving cat", "language": "en" },
		          "amountMicros": "2000000", "currency": "USD", "amountDisplayString": "$2.00", "tier": 1
		        }
		      },
		      "authorDetails": { "channelId": "UCfan", "displayName": "Fan" }
		    },
		    {
		      "id": "milestone",
		      "snippet": {
		        "type": "memberMilestoneChatEvent", "authorChannelId": "UCmember", "publishedAt": "2026-10-05T19:00:04Z",
		        "hasDisplayContent": true, "displayMessage": "half a year",
		        "memberMilestoneChatDetails": { "userComment": "half a year", "memberMonth": 6, "memberLevelName": "Gold" }
		      },
		      "authorDetails": { "channelId": "UCmember", "displayName": "Member", "isChatSponsor": true }
		    },
		    {
		      "id": "sponsor",
		      "snippet": {
		        "type": "newSponsorEvent", "authorChannelId": "UCnew", "publishedAt": "2026-10-05T19:00:05Z",
		        "hasDisplayContent": true, "displayMessage": "Welcome!",
		        "newSponsorDetails": { "memberLevelName": "Gold", "isUpgrade": true }
		      },
		      "authorDetails": { "channelId": "UCnew", "displayName": "Newbie" }
		    },
		    {
		      "id": "gifting",
		      "snippet": {
		        "type": "membershipGiftingEvent", "authorChannelId": "UCgiver", "publishedAt": "2026-10-05T19:00:06Z",
		        "hasDisplayContent": true, "displayMessage": "Gifted 5 memberships",
		        "membershipGiftingDetails": { "giftMembershipsCount": 5, "giftMembershipsLevelName": "Gold" }
		      },
		      "authorDetails": { "channelId": "UCgiver", "displayName": "Giver" }
		    },
		    {
		      "id": "banned",
		      "snippet": {
		        "type": "userBannedEvent", "authorChannelId": "UCowner", "publishedAt": "2026-10-05T19:00:07Z",
		        "hasDisplayContent": true, "displayMessage": "Troll was banned",
		        "userBannedDetails": {
		          "bannedUserDetails": { "channelId": "UCtroll", "channelUrl": "http://www.youtube.com/channel/UCtroll",
		            "displayName": "Troll", "profileImageUrl": "https://yt3.ggpht.com/troll.jpg" },
		          "banType": "temporary", "banDurationSeconds": "300"
		        }
		      },
		      "authorDetails": { "channelId": "UCowner", "displayName": "Owner", "isChatOwner": true }
		    },
		    {
		      "id": "deleted",
		      "snippet": {
		        "type": "messageDeletedEvent", "authorChannelId": "UCowner", "publishedAt": "2026-10-05T19:00:08Z",
		        "hasDisplayContent": false,
		        "messageDeletedDetails": { "deletedMessageId": "text-old" }
		      },
		      "authorDetails": { "channelId": "UCowner", "displayName": "Owner", "isChatOwner": true }
		    },
		    {
		      "id": "retracted",
		      "snippet": {
		        "type": "messageRetractedEvent", "authorChannelId": "UCviewer", "publishedAt": "2026-10-05T19:00:09Z",
		        "hasDisplayContent": false,
		        "messageRetractedDetails": { "retractedMessageId": "text-older" }
		      },
		      "authorDetails": { "channelId": "UCviewer", "displayName": "Viewer" }
		    },
		    {
		      "id": "unknown",
		      "snippet": {
		        "type": "someFutureEvent", "publishedAt": "2026-10-05T19:00:10Z", "hasDisplayContent": false,
		        "someFutureDetails": { "nested": { "value": [1, 2, 3] }, "amount": "99" }
		      }
		    }
		  ]
		}
		""";
}
