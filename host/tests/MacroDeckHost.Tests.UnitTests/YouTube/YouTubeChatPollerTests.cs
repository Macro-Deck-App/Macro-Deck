using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Auth;
using MacroDeckHost.Integrations.YouTube.Protocol;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeChatPollerTests
{
	private const string ChannelId = "UCchannel";

	private FakeYouTubeApiClient _api = null!;
	private YouTubeManualClock _clock = null!;
	private YouTubeQuotaBudget _budget = null!;
	private RecordingYouTubeChatSink _sink = null!;
	private RecordingYouTubeEventPublisher _events = null!;
	private YouTubeAccountConnection _connection = null!;
	private YouTubeChatPoller _poller = null!;
	private int _ended;

	[SetUp]
	public void SetUp()
	{
		_api = new FakeYouTubeApiClient();
		_clock = new YouTubeManualClock(YouTubeTestSupport.Now);
		_budget = new YouTubeQuotaBudget(10_000, _clock);
		_sink = new RecordingYouTubeChatSink();
		_events = new RecordingYouTubeEventPublisher();
		_ended = 0;
		_connection = new YouTubeAccountConnection(YouTubeTestSupport.Account(ChannelId, "My Channel"),
			null,
			null,
			_api,
			_budget,
			new YouTubeEventEmitter(_events),
			YouTubeTestSupport.Silent,
			_sink,
			YouTubeTestSupport.ManualOptions(_clock));
		_poller = new YouTubeChatPoller(_api,
			_budget,
			"chat-1",
			_connection.OnChatMessages,
			() => _ended++,
			() => { },
			YouTubeTestSupport.Silent,
			YouTubeTestSupport.ManualOptions(_clock));
	}

	[TearDown]
	public void TearDown()
	{
		_poller.Dispose();
		_connection.Dispose();
	}

	[TestCase(5000, 5000)]
	[TestCase(200, 1000)]
	public async Task The_next_page_waits_as_long_as_youtube_asks(int pollingIntervalMillis, int expectedMillis)
	{
		_api.ChatPages.Enqueue(new YouTubeChatPage("next", pollingIntervalMillis, null, []));

		var wait = await _poller.NextStepAsync(CancellationToken.None);

		Assert.That(wait, Is.EqualTo(TimeSpan.FromMilliseconds(expectedMillis)));
	}

	[Test]
	public async Task A_low_quota_stretches_the_chat_interval_four_times()
	{
		_budget.Charge(8_500);
		_api.ChatPages.Enqueue(new YouTubeChatPage("next", 2000, null, []));

		var wait = await _poller.NextStepAsync(CancellationToken.None);

		Assert.That(wait, Is.EqualTo(TimeSpan.FromSeconds(8)));
	}

	[Test]
	public async Task A_used_up_quota_pauses_chat_until_the_reset()
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
	public async Task Each_page_continues_from_the_token_of_the_previous_one()
	{
		_api.ChatPages.Enqueue(new YouTubeChatPage("token-2", 1000, null, []));
		_api.ChatPages.Enqueue(new YouTubeChatPage("token-3", 1000, null, []));

		await _poller.NextStepAsync(CancellationToken.None);
		await _poller.NextStepAsync(CancellationToken.None);

		Assert.That(_api.PageTokens, Is.EqualTo(new[] { null, "token-2" }));
	}

	[Test]
	public async Task The_first_page_fills_the_chat_history_without_firing_events()
	{
		_api.ChatPages.Enqueue(new YouTubeChatPage("p2", 1000, null,
		[
			YouTubeTestSupport.Text("old-1", "hello"),
			SuperChat("old-2")
		]));
		_api.ChatPages.Enqueue(new YouTubeChatPage("p3", 1000, null, [SuperChat("new-1")]));

		await _poller.NextStepAsync(CancellationToken.None);
		var eventsAfterHistory = _events.Published.Count;
		await _poller.NextStepAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_sink.Posted.OfType<ChatMessageReceived>().Select(received => received.Message.MessageId),
				Is.EqualTo(new[] { "old-1", "old-2", "new-1" }));
			Assert.That(eventsAfterHistory, Is.Zero);
			Assert.That(_events.Ids, Is.EqualTo(new[] { YouTubeEventIds.SuperChat, YouTubeEventIds.Any }));
		});
	}

	[Test]
	public async Task An_offline_at_time_ends_the_chat()
	{
		_api.ChatPages.Enqueue(new YouTubeChatPage(null, 1000, YouTubeTestSupport.Now, []));

		await _poller.NextStepAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_poller.HasEnded, Is.True);
			Assert.That(_ended, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_chat_ended_message_ends_the_chat()
	{
		_api.ChatPages.Enqueue(new YouTubeChatPage("p2", 1000, null,
			[new YouTubeChatMessage("end", YouTubeChatMessageTypes.ChatEnded, null, null, YouTubeTestSupport.Author())]));

		await _poller.NextStepAsync(CancellationToken.None);

		Assert.That(_poller.HasEnded, Is.True);
	}

	[TestCase("liveChatEnded")]
	[TestCase("liveChatNotFound")]
	[TestCase("liveChatDisabled")]
	public async Task A_chat_youtube_reports_as_gone_ends_the_loop(string reason)
	{
		_api.Failures["chat"] = YouTubeTestSupport.ApiError(reason);

		await _poller.RunAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_poller.HasEnded, Is.True);
			Assert.That(_api.Calls.Count(call => call.StartsWith("chat", StringComparison.Ordinal)), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_transient_failure_retries_later_without_ending_the_chat()
	{
		_api.Failures["chat"] = new YouTubeTransientException("offline");

		var wait = await _poller.NextStepAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_poller.HasEnded, Is.False);
			Assert.That(wait, Is.GreaterThan(TimeSpan.Zero));
		});
	}

	[Test]
	public async Task A_failed_sign_in_refresh_keeps_the_chat_running_and_the_next_page_arrives()
	{
		_api.Failures["chat"] = new YouTubeOAuthTransientException("token endpoint unreachable");

		var wait = await _poller.NextStepAsync(CancellationToken.None);

		_api.Failures.Clear();
		_api.ChatPages.Enqueue(new YouTubeChatPage("next", 5000, null, []));
		await _poller.NextStepAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_poller.HasEnded, Is.False);
			Assert.That(wait, Is.GreaterThan(TimeSpan.Zero));
			Assert.That(_api.Calls.Count(call => call.StartsWith("chat", StringComparison.Ordinal)), Is.EqualTo(2));
		});
	}

	private static YouTubeChatMessage SuperChat(string id)
		=> new(id, YouTubeChatMessageTypes.SuperChat, YouTubeTestSupport.Now, "Great stream!",
			YouTubeTestSupport.Author("UCfan", "Fan"))
		{
			SuperChat = new YouTubeSuperChat(5_000_000, "USD", "$5.00", "Great stream!", 2)
		};
}
