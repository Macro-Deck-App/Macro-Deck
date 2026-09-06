using System.Diagnostics;
using System.Net.WebSockets;
using System.Text.Json;
using MacroDeckHost.Integrations.HomeAssistant.Protocol;

namespace MacroDeckHost.Tests.UnitTests.HomeAssistant;

[TestFixture]
internal sealed class HomeAssistantClientTests
{
	private const string AuthRequired = """{ "type": "auth_required", "ha_version": "2026.8.0" }""";
	private const string AuthOk = """{ "type": "auth_ok", "ha_version": "2026.8.0" }""";
	private const string AuthInvalid = """{ "type": "auth_invalid", "message": "Invalid access token" }""";

	private static readonly Uri _uri = new("ws://127.0.0.1:8123/api/websocket");

	private WebSocketPair _pair = null!;
	private HomeAssistantClient _client = null!;

	[SetUp]
	public async Task SetUp()
	{
		_pair = await WebSocketPair.CreateAsync();
		_client = new HomeAssistantClient((_, _) => Task.FromResult(_pair.Client),
			handshakeTimeout: TimeSpan.FromMilliseconds(300));
	}

	[TearDown]
	public void TearDown()
	{
		_client.Dispose();
		_pair.Dispose();
	}

	[Test]
	public async Task The_handshake_sends_the_token_once_and_returns_the_version()
	{
		await _pair.SendAsync(AuthRequired);
		var connecting = _client.ConnectAsync(_uri, "the-token", CancellationToken.None);

		var raw = await _pair.ReceiveAsync();
		using var request = JsonDocument.Parse(raw);

		Assert.Multiple(() =>
		{
			Assert.That(request.RootElement.GetProperty("type").GetString(), Is.EqualTo("auth"));
			Assert.That(request.RootElement.GetProperty("access_token").GetString(), Is.EqualTo("the-token"));
		});

		await _pair.SendAsync(AuthOk);
		var hello = await connecting;

		Assert.That(hello.Version, Is.EqualTo("2026.8.0"));
	}

	[Test]
	public async Task An_immediate_auth_ok_without_a_greeting_is_tolerated()
	{
		await _pair.SendAsync(AuthOk);

		var hello = await _client.ConnectAsync(_uri, "the-token", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(hello.Version, Is.EqualTo("2026.8.0"));
			Assert.That(_pair.Server.State, Is.EqualTo(WebSocketState.Open));
		});
	}

	[Test]
	public void An_auth_invalid_response_throws_and_names_the_reason()
	{
		var connecting = _client.ConnectAsync(_uri, "wrong-token", CancellationToken.None);

		var exception = Assert.ThrowsAsync<HomeAssistantAuthenticationException>(async () =>
		{
			await _pair.SendAsync(AuthRequired);
			await _pair.ReceiveAsync();
			await _pair.SendAsync(AuthInvalid);
			await connecting;
		});

		Assert.That(exception!.Message, Does.Contain("Invalid access token"));
	}

	[Test]
	public void A_socket_that_drops_mid_handshake_is_distinguishable_from_a_rejected_token()
	{
		var connecting = _client.ConnectAsync(_uri, "the-token", CancellationToken.None);

		var exception = Assert.CatchAsync<Exception>(async () =>
		{
			await _pair.SendAsync(AuthRequired);
			await _pair.ReceiveAsync();
			_pair.Break();
			await connecting;
		});

		// A dropped socket is a HomeAssistantRequestException, never an authentication rejection - the
		// two must not be conflated, since only one of them should stop the reconnect pump for good.
		Assert.That(exception, Is.Not.InstanceOf<HomeAssistantAuthenticationException>());
	}

	[Test]
	public void A_silent_socket_times_out_rather_than_hanging_forever()
	{
		var connecting = _client.ConnectAsync(_uri, "the-token", CancellationToken.None);

		Assert.ThrowsAsync<HomeAssistantRequestException>(async () => await connecting);
	}

	[Test]
	public async Task Command_ids_are_monotonic_starting_from_one()
	{
		await Connect();

		var first = _client.SendCommandAsync("get_states");
		var firstId = await NextRequestIdAsync();
		await _pair.SendAsync($$"""{ "type": "result", "id": {{firstId}}, "success": true, "result": [] }""");
		await first;

		var second = _client.SendCommandAsync("get_config");
		var secondId = await NextRequestIdAsync();
		await _pair.SendAsync($$"""{ "type": "result", "id": {{secondId}}, "success": true, "result": {} }""");
		await second;

		Assert.Multiple(() =>
		{
			Assert.That(firstId, Is.EqualTo(1));
			Assert.That(secondId, Is.EqualTo(2));
		});
	}

	[Test]
	public async Task Concurrent_commands_are_correlated_by_id_not_by_send_order()
	{
		await Connect();

		var first = _client.SendCommandAsync("get_states");
		var firstId = await NextRequestIdAsync();
		var second = _client.SendCommandAsync("get_config");
		var secondId = await NextRequestIdAsync();

		await _pair.SendAsync(
			$$"""{ "type": "result", "id": {{secondId}}, "success": true, "result": { "version": "core-2026.8.0" } }""");
		await _pair.SendAsync($$"""{ "type": "result", "id": {{firstId}}, "success": true, "result": [] }""");

		var secondResult = await second;
		var firstResult = await first;

		Assert.Multiple(() =>
		{
			Assert.That(firstResult.ValueKind, Is.EqualTo(JsonValueKind.Array));
			Assert.That(secondResult.GetProperty("version").GetString(), Is.EqualTo("core-2026.8.0"));
		});
	}

	[Test]
	public async Task A_success_false_result_faults_with_the_home_assistant_code()
	{
		await Connect();

		var pending = _client.SendCommandAsync("call_service");
		var id = await NextRequestIdAsync();
		await _pair.SendAsync(
			$$"""{ "type": "result", "id": {{id}}, "success": false, "error": { "code": "not_found", "message": "Entity not found" } }""");

		var exception = Assert.ThrowsAsync<HomeAssistantRequestException>(async () => await pending);
		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo("not_found"));
			Assert.That(exception.Message, Does.Contain("Entity not found"));
		});
	}

	[Test]
	public async Task An_event_frame_raises_EventReceived_with_its_data_detached()
	{
		await Connect();

		var received
			= new TaskCompletionSource<HomeAssistantEventMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.EventReceived += (_, message) => received.TrySetResult(message);

		await _pair.SendAsync("""
							  { "type": "event", "id": 1,
							    "event": { "event_type": "state_changed", "data": { "entity_id": "light.kitchen" } } }
							  """);

		var message = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.Multiple(() =>
		{
			Assert.That(message.EventType, Is.EqualTo("state_changed"));
			Assert.That(message.Data.GetProperty("entity_id").GetString(), Is.EqualTo("light.kitchen"));
		});
	}

	[Test]
	public async Task State_reported_is_dropped_on_the_read_loop_before_it_reaches_a_subscriber()
	{
		await Connect();

		var seenTypes = new List<string>();
		var afterward
			= new TaskCompletionSource<HomeAssistantEventMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
		_client.EventReceived += (_, message) =>
		{
			seenTypes.Add(message.EventType);
			afterward.TrySetResult(message);
		};

		await _pair.SendAsync(
			"""{ "type": "event", "id": 1, "event": { "event_type": "state_reported", "data": {} } }""");

		await _pair.SendAsync(
			"""{ "type": "event", "id": 1, "event": { "event_type": "state_changed", "data": {} } }""");
		await afterward.Task.WaitAsync(TimeSpan.FromSeconds(5));

		Assert.That(seenTypes, Is.EqualTo(new List<string> { "state_changed" }));
	}

	[Test]
	public async Task A_ping_is_answered_by_the_matching_pong()
	{
		await Connect();

		var pending = _client.SendCommandAsync("ping");
		var id = await NextRequestIdAsync();
		await _pair.SendAsync($$"""{ "type": "pong", "id": {{id}} }""");

		Assert.DoesNotThrowAsync(async () => await pending);
	}

	[Test]
	public async Task A_dropped_connection_faults_every_pending_command_and_reports_it_once()
	{
		await Connect();

		var disconnects = 0;
		_client.Disconnected += (_, _) => Interlocked.Increment(ref disconnects);

		var first = _client.SendCommandAsync("get_states");
		var second = _client.SendCommandAsync("get_config");
		await NextRequestIdAsync();
		await NextRequestIdAsync();
		_pair.Break();

		Assert.ThrowsAsync<HomeAssistantRequestException>(async () => await first);
		Assert.ThrowsAsync<HomeAssistantRequestException>(async () => await second);

		_client.Dispose();
		Assert.That(disconnects, Is.EqualTo(1));
	}

	[Test]
	public async Task A_malformed_message_is_discarded_without_killing_the_session()
	{
		await Connect();

		await _pair.SendAsync("this is not json");

		var pending = _client.SendCommandAsync("get_config");
		var id = await NextRequestIdAsync();
		await _pair.SendAsync($$"""{ "type": "result", "id": {{id}}, "success": true, "result": {} }""");

		Assert.DoesNotThrowAsync(async () => await pending);
	}

	[Test]
	public async Task A_message_past_the_size_limit_closes_the_session()
	{
		await Connect();

		var disconnects = 0;
		_client.Disconnected += (_, _) => Interlocked.Increment(ref disconnects);

		var oversized = "{ \"type\": \"event\", \"id\": 1, \"event\": { \"event_type\": \"state_changed\", " +
			"\"data\": \"" +
			new string('a', HomeAssistantClient.MaxMessageBytes + 1024) +
			"\" } }";

		await _pair.SendAsync(oversized);

		var stopwatch = Stopwatch.StartNew();
		while (disconnects == 0 && stopwatch.Elapsed < TimeSpan.FromSeconds(10))
		{
			await Task.Delay(20);
		}

		Assert.That(disconnects, Is.EqualTo(1));
	}

	private async Task Connect()
	{
		await _pair.SendAsync(AuthRequired);
		var connecting = _client.ConnectAsync(_uri, "the-token", CancellationToken.None);
		await _pair.ReceiveAsync();
		await _pair.SendAsync(AuthOk);
		await connecting;
	}

	private async Task<int> NextRequestIdAsync()
	{
		var raw = await _pair.ReceiveAsync();
		using var request = JsonDocument.Parse(raw);
		return request.RootElement.GetProperty("id").GetInt32();
	}
}
