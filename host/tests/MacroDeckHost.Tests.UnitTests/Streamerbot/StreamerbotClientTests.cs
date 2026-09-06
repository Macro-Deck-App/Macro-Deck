using System.Text.Json;
using MacroDeckHost.Integrations.Streamerbot.Protocol;

namespace MacroDeckHost.Tests.UnitTests.Streamerbot;

[TestFixture]
internal sealed class StreamerbotClientTests
{
	private const string Hello =
		"""
		{ "request": "Hello", "timestamp": "2026-07-30T10:00:00Z", "session": "s1",
		  "info": { "instanceId": "instance-1", "name": "Streamer.bot", "version": "0.2.5", "os": "windows" },
		  "authentication": { "salt": "c2FsdA==", "challenge": "Y2hhbGxlbmdl" } }
		""";

	private static readonly Uri _uri = new("ws://127.0.0.1:8080/");

	private WebSocketPair _pair = null!;
	private StreamerbotClient _client = null!;

	[SetUp]
	public async Task SetUp()
	{
		_pair = await WebSocketPair.CreateAsync();
		_client = new StreamerbotClient((_, _) => Task.FromResult(_pair.Client),
			helloTimeout: TimeSpan.FromMilliseconds(300));
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_pair.Dispose();
	}

	[Test]
	public async Task ConnectAsync_reads_the_greeting_and_its_challenge()
	{
		await _pair.SendAsync(Hello);

		var hello = await _client.ConnectAsync(_uri, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(hello?.Info?.Version, Is.EqualTo("0.2.5"));
			Assert.That(hello?.Info?.Name, Is.EqualTo("Streamer.bot"));
			Assert.That(hello?.Authentication?.Salt, Is.EqualTo("c2FsdA=="));
			Assert.That(hello?.Authentication?.Challenge, Is.EqualTo("Y2hhbGxlbmdl"));
		});
	}

	[Test]
	public async Task ConnectAsync_answers_null_when_the_server_does_not_greet()
	{
		var hello = await _client.ConnectAsync(_uri, CancellationToken.None);

		Assert.That(hello, Is.Null);
	}

	[Test]
	public async Task ConnectAsync_reads_a_greeting_without_authentication()
	{
		await _pair.SendAsync("""{ "request": "Hello", "info": { "name": "Streamer.bot", "version": "0.2.5" } }""");

		var hello = await _client.ConnectAsync(_uri, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(hello?.Info?.Version, Is.EqualTo("0.2.5"));
			Assert.That(hello?.Authentication, Is.Null);
		});
	}

	[Test]
	public async Task A_request_carries_a_name_and_an_id_and_is_matched_by_it()
	{
		await _pair.SendAsync(Hello);
		await _client.ConnectAsync(_uri, CancellationToken.None);

		var pending = _client.RequestAsync("GetActions");

		var raw = await _pair.ReceiveAsync();
		using var request = JsonDocument.Parse(raw);
		var id = request.RootElement.GetProperty("id").GetString();

		Assert.That(request.RootElement.GetProperty("request").GetString(), Is.EqualTo("GetActions"));

		await _pair.SendAsync($$"""{ "status": "ok", "id": "{{id}}", "count": 0, "actions": [] }""");

		var response = await pending;
		Assert.That(response.GetProperty("count").GetInt32(), Is.Zero);
	}

	[Test]
	public async Task A_request_payload_is_merged_into_the_body()
	{
		await _pair.SendAsync(Hello);
		await _client.ConnectAsync(_uri, CancellationToken.None);

		var pending = _client.RequestAsync("DoAction",
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["action"] = new Dictionary<string, object?>(StringComparer.Ordinal) { ["name"] = "Shoutout" },
				["ignored"] = null
			});

		var raw = await _pair.ReceiveAsync();
		using var request = JsonDocument.Parse(raw);

		Assert.Multiple(() =>
		{
			Assert.That(request.RootElement.GetProperty("action").GetProperty("name").GetString(),
				Is.EqualTo("Shoutout"));
			Assert.That(request.RootElement.TryGetProperty("ignored", out _), Is.False);
		});

		await _pair.SendAsync(
			$$"""{ "status": "ok", "id": "{{request.RootElement.GetProperty("id").GetString()}}" }""");
		await pending;
	}

	[Test]
	public async Task A_refused_request_throws()
	{
		await _pair.SendAsync(Hello);
		await _client.ConnectAsync(_uri, CancellationToken.None);

		var pending = _client.RequestAsync("Authenticate");
		var raw = await _pair.ReceiveAsync();
		using var request = JsonDocument.Parse(raw);
		var id = request.RootElement.GetProperty("id").GetString();

		await _pair.SendAsync($$"""{ "status": "error", "id": "{{id}}", "error": "Authentication failed" }""");

		var exception = Assert.ThrowsAsync<StreamerbotRequestException>(async () => await pending);
		Assert.That(exception!.Message, Does.Contain("Authentication failed"));
	}

	[Test]
	public async Task A_pushed_event_is_raised_with_its_data_detached()
	{
		await _pair.SendAsync(Hello);
		await _client.ConnectAsync(_uri, CancellationToken.None);

		var received
			= new TaskCompletionSource<StreamerbotEventMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.EventReceived += (_, message) => received.TrySetResult(message);

		await _pair.SendAsync("""
							  { "timeStamp": "2026-07-30T10:00:00Z", "event": { "source": "Twitch", "type": "Follow" },
							    "data": { "user_name": "Ada" } }
							  """);

		var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(message.Source, Is.EqualTo("Twitch"));
			Assert.That(message.Type, Is.EqualTo("Follow"));
			Assert.That(message.Data.GetProperty("user_name").GetString(), Is.EqualTo("Ada"));
		});
	}

	[Test]
	public async Task A_malformed_message_is_discarded_without_killing_the_session()
	{
		await _pair.SendAsync(Hello);
		await _client.ConnectAsync(_uri, CancellationToken.None);

		await _pair.SendAsync("this is not json");

		var pending = _client.RequestAsync("GetInfo");
		var raw = await _pair.ReceiveAsync();
		using var request = JsonDocument.Parse(raw);
		await _pair.SendAsync(
			$$"""{ "status": "ok", "id": "{{request.RootElement.GetProperty("id").GetString()}}" }""");

		Assert.DoesNotThrowAsync(async () => await pending);
	}

	[Test]
	public async Task A_dropped_connection_fails_the_pending_requests_and_reports_it_once()
	{
		await _pair.SendAsync(Hello);
		await _client.ConnectAsync(_uri, CancellationToken.None);

		var disconnects = 0;
		_client.Disconnected += (_, _) => Interlocked.Increment(ref disconnects);

		var pending = _client.RequestAsync("GetActions");
		await _pair.ReceiveAsync();
		_pair.Break();

		Assert.ThrowsAsync<StreamerbotRequestException>(async () => await pending);

		_client.Dispose();
		Assert.That(disconnects, Is.EqualTo(1));
	}

	[Test]
	public async Task A_request_before_connecting_is_refused()
	{
		var exception
			= Assert.ThrowsAsync<StreamerbotRequestException>(async () => await _client.RequestAsync("GetInfo"));

		Assert.That(exception!.Message, Does.Contain("not connected"));
		await Task.CompletedTask;
	}
}
