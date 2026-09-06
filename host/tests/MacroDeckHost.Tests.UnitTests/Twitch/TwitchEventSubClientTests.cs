using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MacroDeckHost.Integrations.Twitch.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Twitch;

[TestFixture]
internal sealed class TwitchEventSubClientTests
{
	private static readonly Uri _endpoint = new("wss://eventsub.wss.twitch.tv/ws");

	private WebSocketPair _pair = null!;
	private TwitchEventSubClient _client = null!;

	[SetUp]
	public async Task SetUp()
	{
		_pair = await WebSocketPair.CreateAsync();
		_client = new TwitchEventSubClient((_, _) => Task.FromResult(_pair.Client), TimeSpan.FromSeconds(5));
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_pair.Dispose();
	}

	[Test]
	public async Task The_welcome_carries_the_session_id_and_the_keepalive()
	{
		var connect = _client.ConnectAsync(_endpoint, CancellationToken.None);
		await _pair.SendAsync(Welcome("session-42", keepalive: 10));

		var welcome = await connect;

		Assert.Multiple(() =>
		{
			Assert.That(welcome.SessionId, Is.EqualTo("session-42"));
			Assert.That(welcome.Keepalive, Is.EqualTo(TimeSpan.FromSeconds(10)));
			Assert.That(_client.IsConnected, Is.True);
		});
	}

	[Test]
	public async Task A_notification_arrives_with_its_metadata_and_payload()
	{
		await ConnectAsync();
		var received
			= new TaskCompletionSource<TwitchEventSubMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.MessageReceived += (_, message) => received.TrySetResult(message);

		await _pair.SendAsync(Notification("msg-1", "channel.follow", "2", """{"event":{"user_login":"viewer"}}"""));

		var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Multiple(() =>
		{
			Assert.That(message.MessageId, Is.EqualTo("msg-1"));
			Assert.That(message.MessageType, Is.EqualTo(TwitchEventSubMessageTypes.Notification));
			Assert.That(message.SubscriptionType, Is.EqualTo("channel.follow"));
			Assert.That(message.SubscriptionVersion, Is.EqualTo("2"));
			Assert.That(message.Payload.GetProperty("event").GetProperty("user_login").GetString(),
				Is.EqualTo("viewer"));
		});
	}

	[Test]
	public async Task A_keepalive_is_delivered_so_the_watchdog_can_see_it()
	{
		await ConnectAsync();
		var received
			= new TaskCompletionSource<TwitchEventSubMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.MessageReceived += (_, message) => received.TrySetResult(message);

		await _pair.SendAsync(
			"""{"metadata":{"message_id":"k1","message_type":"session_keepalive","message_timestamp":"2026-07-30T12:00:00Z"},"payload":{}}""");

		var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(message.MessageType, Is.EqualTo(TwitchEventSubMessageTypes.Keepalive));
	}

	[Test]
	public async Task A_malformed_message_is_discarded_without_ending_the_session()
	{
		await ConnectAsync();
		var received
			= new TaskCompletionSource<TwitchEventSubMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.MessageReceived += (_, message) => received.TrySetResult(message);

		await _pair.SendAsync("not json at all");
		await _pair.SendAsync(Notification("msg-1", "stream.online", "1", """{"event":{}}"""));

		var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.Multiple(() =>
		{
			Assert.That(message.MessageId, Is.EqualTo("msg-1"));
			Assert.That(_client.IsConnected, Is.True);
		});
	}

	[Test]
	public async Task A_dropped_connection_raises_disconnected()
	{
		await ConnectAsync();
		var disconnected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.Disconnected += (_, reason) => disconnected.TrySetResult(reason);

		_pair.Break();

		Assert.That(async () => await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5)), Throws.Nothing);
	}

	[Test]
	public async Task A_close_code_is_reported_to_the_session()
	{
		await ConnectAsync();
		var disconnected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.Disconnected += (_, reason) => disconnected.TrySetResult(reason);

		await _pair.Server.CloseOutputAsync((WebSocketCloseStatus)4003,
			"subscription missing",
			CancellationToken.None);

		var reason = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(reason, Does.Contain("4003"));
	}

	[Test]
	public async Task An_oversized_frame_ends_the_session()
	{
		await ConnectAsync();
		var disconnected = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.Disconnected += (_, reason) => disconnected.TrySetResult(reason);

		var oversized = new string('x', TwitchEventSubClient.MaxMessageBytes + 1024);
		await _pair.Server.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(oversized)),
			WebSocketMessageType.Text,
			endOfMessage: true,
			CancellationToken.None);

		var reason = await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Assert.That(reason, Does.Contain("size limit"));
	}

	[Test]
	public void A_socket_that_dies_before_the_welcome_fails_the_connect()
	{
		var connect = _client.ConnectAsync(_endpoint, CancellationToken.None);
		_pair.Break();

		Assert.ThrowsAsync<TwitchEventSubException>(async () => await connect.WaitAsync(TimeSpan.FromSeconds(5)));
	}

	[Test]
	public async Task A_session_cannot_be_reused()
	{
		await ConnectAsync();

		Assert.ThrowsAsync<InvalidOperationException>(() => _client.ConnectAsync(_endpoint, CancellationToken.None));
	}

	private async Task ConnectAsync()
	{
		var connect = _client.ConnectAsync(_endpoint, CancellationToken.None);
		await _pair.SendAsync(Welcome("session-1", keepalive: 30));
		await connect;
	}

	private static string Welcome(string sessionId, int keepalive)
		=> JsonSerializer.Serialize(new
		{
			metadata = new
			{
				message_id = "welcome-1",
				message_type = TwitchEventSubMessageTypes.Welcome,
				message_timestamp = "2026-07-30T12:00:00Z"
			},
			payload = new
			{
				session = new
				{
					id = sessionId,
					status = "connected",
					keepalive_timeout_seconds = keepalive
				}
			}
		});

	private static string Notification(string messageId, string type, string version, string payload)
		=> $$"""
			 {"metadata":{"message_id":"{{messageId}}","message_type":"notification",
			  "message_timestamp":"2026-07-30T12:00:00Z","subscription_type":"{{type}}",
			  "subscription_version":"{{version}}"},"payload":{{payload}}}
			 """;
}
