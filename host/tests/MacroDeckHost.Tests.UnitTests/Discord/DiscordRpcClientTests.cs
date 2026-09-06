using System.Text;
using System.Text.Json;
using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordRpcClientTests
{
	[Test]
	public async Task Connecting_sends_the_handshake_and_returns_the_ready_payload()
	{
		var transport = new FakeDiscordIpcTransport();
		using var client = new DiscordRpcClient(transport);

		var ready = await client.ConnectAsync("123456", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(client.IsConnected, Is.True);
			Assert.That(ready.GetProperty("user").GetProperty("username").GetString(), Is.EqualTo("tester"));
		});

		var handshake = transport.WrittenWithOpcode(DiscordRpcOpcode.Handshake).Single();
		Assert.That(Encoding.UTF8.GetString(handshake.Payload), Is.EqualTo("""{"v":1,"client_id":"123456"}"""));
	}

	[Test]
	public async Task A_command_is_answered_with_its_own_response()
	{
		var transport = new FakeDiscordIpcTransport
		{
			Responder = command => command.Command == "GET_VOICE_SETTINGS"
				? FakeResponse.Ok("""{"mute":true}""")
				: FakeResponse.Ok()
		};
		using var client = new DiscordRpcClient(transport);
		await client.ConnectAsync("1", CancellationToken.None);

		var response = await client.SendCommandAsync("GET_VOICE_SETTINGS");

		Assert.That(response.GetProperty("mute").GetBoolean(), Is.True);
	}

	[Test]
	public async Task Concurrent_commands_are_matched_by_nonce()
	{
		var transport = new FakeDiscordIpcTransport
		{
			Responder = command => FakeResponse.Ok($$"""{"echo":"{{command.Command}}"}""")
		};
		using var client = new DiscordRpcClient(transport);
		await client.ConnectAsync("1", CancellationToken.None);

		var first = client.SendCommandAsync("GET_GUILDS");
		var second = client.SendCommandAsync("GET_VOICE_SETTINGS");

		Assert.Multiple(async () =>
		{
			Assert.That((await first).GetProperty("echo").GetString(), Is.EqualTo("GET_GUILDS"));
			Assert.That((await second).GetProperty("echo").GetString(), Is.EqualTo("GET_VOICE_SETTINGS"));
		});
	}

	[Test]
	public async Task A_subscribe_carries_the_event_name_and_its_arguments()
	{
		var transport = new FakeDiscordIpcTransport();
		using var client = new DiscordRpcClient(transport);
		await client.ConnectAsync("1", CancellationToken.None);

		await client.SendCommandAsync("SUBSCRIBE", new { channel_id = "42" }, "VOICE_STATE_UPDATE");

		var command = transport.Commands.Single();
		Assert.Multiple(() =>
		{
			Assert.That(command.Command, Is.EqualTo("SUBSCRIBE"));
			Assert.That(command.EventName, Is.EqualTo("VOICE_STATE_UPDATE"));
			Assert.That(command.Payload, Does.Contain("""{"channel_id":"42"}"""));
		});
	}

	[Test]
	public async Task An_error_response_faults_only_the_command_it_answers()
	{
		var transport = new FakeDiscordIpcTransport
		{
			Responder = command => command.Command == "AUTHENTICATE"
				? FakeResponse.Error(4009, "Invalid token")
				: FakeResponse.Ok()
		};
		using var client = new DiscordRpcClient(transport);
		await client.ConnectAsync("1", CancellationToken.None);

		var exception
			= Assert.ThrowsAsync<DiscordRpcException>(async () => await client.SendCommandAsync("AUTHENTICATE"));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.Code, Is.EqualTo(4009));
			Assert.That(exception.IsAuthProblem, Is.True);
			Assert.That(client.IsConnected, Is.True);
		});
	}

	[Test]
	public async Task A_scope_error_is_recognised_as_one()
	{
		var transport = new FakeDiscordIpcTransport { Responder = _ => FakeResponse.Error(4007, "Invalid scope") };
		using var client = new DiscordRpcClient(transport);
		await client.ConnectAsync("1", CancellationToken.None);

		var exception = Assert.ThrowsAsync<DiscordRpcException>(async () => await client.SendCommandAsync("AUTHORIZE"));

		Assert.That(exception!.IsScopeProblem, Is.True);
	}

	[Test]
	public async Task Events_without_a_nonce_are_dispatched_to_subscribers()
	{
		var transport = new FakeDiscordIpcTransport();
		using var client = new DiscordRpcClient(transport);
		var received = new List<DiscordRpcEventArgs>();
		client.EventReceived += (_, e) => received.Add(e);

		await client.ConnectAsync("1", CancellationToken.None);
		transport.PushEvent("VOICE_SETTINGS_UPDATE", """{"mute":true}""");

		await WaitForAsync(() => received.Count > 0);

		Assert.Multiple(() =>
		{
			Assert.That(received[0].EventName, Is.EqualTo("VOICE_SETTINGS_UPDATE"));
			Assert.That(received[0].Data.GetProperty("mute").GetBoolean(), Is.True);
		});
	}

	[Test]
	public async Task A_ping_is_answered_with_a_pong_carrying_the_same_payload()
	{
		var transport = new FakeDiscordIpcTransport();
		using var client = new DiscordRpcClient(transport);
		await client.ConnectAsync("1", CancellationToken.None);

		transport.Push(DiscordRpcOpcode.Ping, """{"nonce":"abc"}""");

		await WaitForAsync(() => transport.WrittenWithOpcode(DiscordRpcOpcode.Pong).Any());

		var pong = transport.WrittenWithOpcode(DiscordRpcOpcode.Pong).Single();
		Assert.That(Encoding.UTF8.GetString(pong.Payload), Is.EqualTo("""{"nonce":"abc"}"""));
	}

	[Test]
	public async Task A_closed_stream_reports_a_disconnect()
	{
		var transport = new FakeDiscordIpcTransport();
		using var client = new DiscordRpcClient(transport);
		string? reason = null;
		client.Disconnected += (_, r) => reason = r;

		await client.ConnectAsync("1", CancellationToken.None);
		transport.Close();

		await WaitForAsync(() => reason is not null);

		Assert.Multiple(() =>
		{
			Assert.That(reason, Is.EqualTo("Discord closed the connection"));
			Assert.That(client.IsConnected, Is.False);
		});
	}

	[Test]
	public async Task A_close_frame_reports_the_reason_discord_gave()
	{
		var transport = new FakeDiscordIpcTransport();
		using var client = new DiscordRpcClient(transport);
		string? reason = null;
		client.Disconnected += (_, r) => reason = r;

		await client.ConnectAsync("1", CancellationToken.None);
		transport.Push(DiscordRpcOpcode.Close, """{"code":4000,"message":"Invalid client id"}""");

		await WaitForAsync(() => reason is not null);

		Assert.That(reason, Is.EqualTo("Invalid client id"));
	}

	[Test]
	public async Task Losing_the_connection_faults_pending_commands()
	{
		var transport = new FakeDiscordIpcTransport { Responder = _ => null };
		using var client = new DiscordRpcClient(transport);
		await client.ConnectAsync("1", CancellationToken.None);

		var pending = client.SendCommandAsync("GET_GUILDS");
		await WaitForAsync(() => transport.Commands.Count > 0);
		transport.Close();

		Assert.ThrowsAsync<DiscordRpcException>(async () => await pending);
	}

	[Test]
	public async Task A_connection_level_error_faults_the_handshake()
	{
		var transport = new FakeDiscordIpcTransport { AutoReady = false };
		using var client = new DiscordRpcClient(transport);

		var connecting = client.ConnectAsync("1", CancellationToken.None);
		await WaitForAsync(() => transport.WrittenWithOpcode(DiscordRpcOpcode.Handshake).Any());
		transport.Push(DiscordRpcOpcode.Frame,
			"""{"cmd":"DISPATCH","evt":"ERROR","data":{"code":4000,"message":"Invalid client id"}}""");

		var exception = Assert.ThrowsAsync<DiscordRpcException>(async () => await connecting);
		Assert.That(exception!.Message, Is.EqualTo("Invalid client id"));
	}

	[Test]
	public void Sending_before_connecting_fails_fast()
	{
		var transport = new FakeDiscordIpcTransport();
		using var client = new DiscordRpcClient(transport);

		Assert.ThrowsAsync<DiscordRpcException>(async () => await client.SendCommandAsync("GET_GUILDS"));
	}

	[Test]
	public async Task A_malformed_frame_is_discarded_without_dropping_the_connection()
	{
		var transport = new FakeDiscordIpcTransport();
		using var client = new DiscordRpcClient(transport);
		var disconnected = false;
		client.Disconnected += (_, _) => disconnected = true;

		await client.ConnectAsync("1", CancellationToken.None);
		transport.Push(DiscordRpcOpcode.Frame, "not json at all");
		transport.PushEvent("VOICE_SETTINGS_UPDATE", """{"mute":false}""");

		var received = 0;
		client.EventReceived += (_, _) => received++;
		transport.PushEvent("VOICE_SETTINGS_UPDATE", """{"mute":true}""");

		await WaitForAsync(() => received > 0);
		Assert.That(disconnected, Is.False);
	}

	[Test]
	public void The_serializer_omits_nulls_and_uses_snake_case()
	{
		var json = JsonSerializer.Serialize(new DiscordVoiceSettingsPatch { Deaf = true },
			DiscordRpcClient.SerializerOptions);

		Assert.That(json, Is.EqualTo("""{"deaf":true}"""));
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		for (var attempt = 0; attempt < 200 && !condition(); attempt++)
		{
			await Task.Delay(10);
		}

		Assert.That(condition(), Is.True, "The awaited condition did not become true in time.");
	}
}
