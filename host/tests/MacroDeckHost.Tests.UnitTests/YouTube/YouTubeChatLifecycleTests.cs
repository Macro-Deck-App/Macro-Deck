using MacroDeckHost.Application.StreamChat;
using MacroDeckHost.Integrations.YouTube;
using MacroDeckHost.Integrations.YouTube.Protocol;

namespace MacroDeckHost.Tests.UnitTests.YouTube;

[TestFixture]
internal sealed class YouTubeChatLifecycleTests
{
	private const string ChannelId = "UCchannel";

	private static readonly TimeSpan _patience = TimeSpan.FromSeconds(5);

	private FakeYouTubeApiClient _api = null!;
	private RecordingYouTubeChatSink _sink = null!;
	private YouTubeAccountConnection _connection = null!;

	[SetUp]
	public void SetUp()
	{
		_api = new FakeYouTubeApiClient();
		_sink = new RecordingYouTubeChatSink();
		var clock = new YouTubeManualClock(YouTubeTestSupport.Now);
		_connection = new YouTubeAccountConnection(YouTubeTestSupport.Account(ChannelId, "My Channel"),
			null,
			null,
			_api,
			new YouTubeQuotaBudget(10_000, clock),
			new YouTubeEventEmitter(new RecordingYouTubeEventPublisher()),
			YouTubeTestSupport.Silent,
			_sink,
			YouTubeTestSupport.ManualOptions(clock));
	}

	[TearDown]
	public void TearDown() => _connection.Dispose();

	[Test]
	public async Task Going_live_connects_the_chat_and_its_messages_reach_the_widget()
	{
		_api.ChatPages.Enqueue(new YouTubeChatPage("next", 5000, null,
			[YouTubeTestSupport.Text("m1", "Hello stream")]));

		_connection.OnPolled(Live("chat-1"));

		await WaitUntil(() => _sink.Posted.OfType<ChatMessageReceived>().Any());

		Assert.Multiple(() =>
		{
			Assert.That(_sink.Posted.OfType<ChatConnectionChanged>().Select(change => change.IsConnected),
				Is.EqualTo(new[] { true }));
			Assert.That(_sink.Posted.OfType<ChatMessageReceived>().Single().Message.PlainText,
				Does.Contain("Hello stream"));
		});
	}

	[Test]
	public async Task Going_offline_disconnects_the_chat_and_stops_reading_it()
	{
		_connection.OnPolled(Live("chat-1"));
		await WaitUntil(() => ChatCalls() == 1);

		_connection.OnPolled(YouTubeAccountState.Unknown with { IsLive = false });

		Assert.Multiple(() =>
		{
			Assert.That(_sink.Posted.OfType<ChatConnectionChanged>().Select(change => change.IsConnected),
				Is.EqualTo(new[] { true, false }));
			Assert.That(ChatCalls(), Is.EqualTo(1));
		});
	}

	[Test]
	public async Task An_ended_chat_is_not_read_again_while_the_broadcast_still_reports_it()
	{
		_api.ChatPages.Enqueue(new YouTubeChatPage(null, 5000, YouTubeTestSupport.Now, []));

		_connection.OnPolled(Live("chat-1"));
		await WaitUntil(() => _sink.Posted.OfType<ChatConnectionChanged>().Count() == 2);

		_connection.OnPolled(Live("chat-1"));

		Assert.Multiple(() =>
		{
			Assert.That(ChatCalls(), Is.EqualTo(1));
			Assert.That(_sink.Posted.OfType<ChatConnectionChanged>().Last().IsConnected, Is.False);
		});
	}

	private int ChatCalls() => _api.Calls.Count(call => call.StartsWith("chat:", StringComparison.Ordinal));

	private static YouTubeAccountState Live(string liveChatId)
		=> YouTubeAccountState.Unknown with { IsLive = true, BroadcastId = "b1", LiveChatId = liveChatId };

	private static async Task WaitUntil(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow + _patience;
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The chat did not reach the expected state in time.");
			}

			await Task.Delay(10);
		}
	}
}
