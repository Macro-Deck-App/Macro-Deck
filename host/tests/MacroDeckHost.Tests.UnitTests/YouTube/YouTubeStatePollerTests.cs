using System.Net;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Protocol;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeStatePollerTests
{
	private FakeYouTubeApiClient _api = null!;
	private YouTubeManualClock _clock = null!;
	private YouTubeQuotaBudget _budget = null!;
	private YouTubeAccountState _state = null!;
	private List<YouTubeLiveTransition> _transitions = null!;
	private YouTubeStatePoller _poller = null!;

	[SetUp]
	public void SetUp()
	{
		_api = new FakeYouTubeApiClient();
		_clock = new YouTubeManualClock(YouTubeTestSupport.Now);
		_budget = new YouTubeQuotaBudget(10_000, _clock);
		_state = YouTubeAccountState.Unknown;
		_transitions = [];
		_poller = Poller(_api);
	}

	[TearDown]
	public void TearDown() => _poller.Dispose();

	[Test]
	public async Task A_live_persistent_broadcast_counts_as_live()
	{
		_api.Active.Add(YouTubeTestSupport.Broadcast("persistent", YouTubeLifeCycleStatus.Live, "chat-1",
			enableAutoStart: true));
		_api.Videos["persistent"] = YouTubeTestSupport.Video("persistent", viewers: 42, likes: 7, title: "Stream now");

		await _poller.PollOnceAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_api.Calls, Does.Contain("broadcasts:active"));
			Assert.That(_state.IsLive, Is.True);
			Assert.That(_state.BroadcastId, Is.EqualTo("persistent"));
			Assert.That(_state.LiveChatId, Is.EqualTo("chat-1"));
			Assert.That(_state.StreamTitle, Is.EqualTo("Stream now"));
			Assert.That(_state.ViewerCount, Is.EqualTo(42));
			Assert.That(_state.LikeCount, Is.EqualTo(7));
			Assert.That(_state.StreamThumbnailUrl, Does.StartWith("https://i.ytimg.com/vi/"));
		});
	}

	[TestCase(YouTubeLifeCycleStatus.LiveStarting)]
	[TestCase(YouTubeLifeCycleStatus.Testing)]
	[TestCase(YouTubeLifeCycleStatus.TestStarting)]
	public async Task A_broadcast_that_is_not_live_yet_does_not_count_as_live(string lifeCycleStatus)
	{
		_api.Active.Add(YouTubeTestSupport.Broadcast("b1", lifeCycleStatus, "chat-1"));
		_api.Videos["b1"] = YouTubeTestSupport.Video("b1");

		await _poller.PollOnceAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_state.IsLive, Is.False);
			Assert.That(_state.LiveChatId, Is.Null);
			Assert.That(_api.Calls, Has.None.StartsWith("video:"), "no video read is spent while not live");
		});
	}

	[Test]
	public async Task Hidden_subscribers_and_absent_concurrent_viewers_read_as_unavailable()
	{
		using var handler = new YouTubeFakeHttpHandler()
			.Enqueue(HttpStatusCode.OK,
				"""{"items":[{"id":"b1","snippet":{"title":"Live","liveChatId":"chat-1","actualStartTime":"2026-10-05T18:30:00Z"},"status":{"lifeCycleStatus":"live"},"contentDetails":{"enableAutoStart":false}}]}""")
			.Enqueue(HttpStatusCode.OK,
				"""{"items":[{"id":"b1","snippet":{"title":"Live","categoryId":"20"},"statistics":{"likeCount":"5"},"liveStreamingDetails":{"actualStartTime":"2026-10-05T18:30:00Z","activeLiveChatId":"chat-1"}}]}""")
			.Enqueue(HttpStatusCode.OK,
				"""{"items":[{"id":"UCchannel","snippet":{"title":"Channel","customUrl":"@channel"},"statistics":{"hiddenSubscriberCount":true,"subscriberCount":"0"}}]}""");
		using var client = new YouTubeApiClient(handler,
			_ => Task.FromResult("token"),
			_ => Task.CompletedTask,
			_budget,
			YouTubeTestSupport.Silent);
		using var poller = Poller(client);

		await poller.PollOnceAsync(CancellationToken.None);

		var account = YouTubeTestSupport.Account();
		Assert.Multiple(() =>
		{
			Assert.That(_state.IsLive, Is.True);
			Assert.That(YouTubeVariables.Read(account, _state, "viewer_count", _clock.Now), Is.Null);
			Assert.That(YouTubeVariables.Read(account, _state, "subscriber_count", _clock.Now), Is.Null);
			Assert.That(YouTubeVariables.Read(account, _state, "like_count", _clock.Now), Is.EqualTo(5));
			Assert.That(YouTubeVariables.Read(account, _state, "uptime_seconds", _clock.Now), Is.EqualTo(1800));
		});
	}

	[Test]
	public async Task The_first_poll_only_learns_the_state_and_later_changes_fire_once_each()
	{
		var live = YouTubeTestSupport.Broadcast("b1", YouTubeLifeCycleStatus.Live, "chat-1");
		_api.Videos["b1"] = YouTubeTestSupport.Video("b1");

		_api.Active.Add(live);
		await _poller.PollOnceAsync(CancellationToken.None);
		await _poller.PollOnceAsync(CancellationToken.None);
		var afterPriming = _transitions.Count;

		_api.Active.Clear();
		await _poller.PollOnceAsync(CancellationToken.None);
		await _poller.PollOnceAsync(CancellationToken.None);

		_api.Active.Add(live);
		await _poller.PollOnceAsync(CancellationToken.None);
		await _poller.PollOnceAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(afterPriming, Is.Zero, "a host started mid-stream must not announce the stream");
			Assert.That(_transitions.Select(transition => transition.IsLive), Is.EqualTo(new[] { false, true }));
		});
	}

	[Test]
	public async Task A_failed_first_poll_does_not_prime()
	{
		_api.Failures["broadcasts"] = new YouTubeTransientException("offline");
		await _poller.PollOnceAsync(CancellationToken.None);
		_api.Failures.Clear();

		_api.Active.Add(YouTubeTestSupport.Broadcast("b1", YouTubeLifeCycleStatus.Live, "chat-1"));
		await _poller.PollOnceAsync(CancellationToken.None);

		Assert.That(_transitions, Is.Empty);
	}

	[Test]
	public async Task The_channel_is_read_every_ten_minutes_not_every_poll()
	{
		await _poller.PollOnceAsync(CancellationToken.None);
		_clock.Now += TimeSpan.FromMinutes(5);
		await _poller.PollOnceAsync(CancellationToken.None);
		_clock.Now += TimeSpan.FromMinutes(5);
		await _poller.PollOnceAsync(CancellationToken.None);

		Assert.That(_api.Calls.Count(call => call == "channel"), Is.EqualTo(2));
	}

	[Test]
	public async Task The_state_is_polled_every_minute_and_every_two_minutes_once_the_quota_runs_low()
	{
		var normal = await _poller.NextStepAsync(CancellationToken.None);
		_budget.Charge(8_000);
		var throttled = await _poller.NextStepAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(normal, Is.EqualTo(TimeSpan.FromSeconds(60)));
			Assert.That(throttled, Is.EqualTo(TimeSpan.FromMinutes(2)));
		});
	}

	[Test]
	public async Task A_used_up_quota_pauses_polling_until_google_resets_it()
	{
		_budget.MarkExhausted();

		var wait = await _poller.NextStepAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(wait, Is.EqualTo(_budget.ResetsAt - _clock.Now));
			Assert.That(_api.Calls, Is.Empty);
		});
	}

	[Test]
	public async Task A_quota_answer_from_youtube_pauses_polling()
	{
		using var handler = new YouTubeFakeHttpHandler().Enqueue(HttpStatusCode.Forbidden,
			"""{"error":{"code":403,"message":"quota","errors":[{"reason":"quotaExceeded","domain":"youtube.quota"}]}}""");
		using var client = new YouTubeApiClient(handler,
			_ => Task.FromResult("token"),
			_ => Task.CompletedTask,
			_budget,
			YouTubeTestSupport.Silent);
		using var poller = Poller(client);

		var wait = await poller.NextStepAsync(CancellationToken.None);

		Assert.That(wait, Is.EqualTo(_budget.ResetsAt - _clock.Now));
	}

	private YouTubeStatePoller Poller(IYouTubeApiClient api)
		=> new(api,
			_budget,
			YouTubeTestSupport.Account(),
			update => _state = update(_state),
			_transitions.Add,
			_ => { },
			() => { },
			YouTubeTestSupport.Silent,
			YouTubeTestSupport.ManualOptions(_clock));
}
