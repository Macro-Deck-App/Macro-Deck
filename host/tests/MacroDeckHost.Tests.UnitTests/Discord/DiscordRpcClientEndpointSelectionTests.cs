using System.Text.Json;
using MacroDeckHost.Integrations.Discord;
using MacroDeckHost.Integrations.Discord.Rpc;

namespace MacroDeckHost.Tests.UnitTests.Discord;

[TestFixture]
internal sealed class DiscordRpcClientEndpointSelectionTests
{
	private const string ArRpcReady =
		"""{"v":1,"user":{"id":"1045800378228281345","username":"arrpc","global_name":"arRPC"}}""";

	private static readonly TimeSpan _probeTimeout = TimeSpan.FromMilliseconds(200);

	[Test]
	public async Task A_rich_presence_server_announcing_the_arrpc_user_is_passed_over_for_discord()
	{
		var endpoints = new FakeDiscordIpcEndpoints(
			() => new FakeDiscordIpcTransport { Name = "discord-ipc-0", ReadyData = ArRpcReady },
			() => new FakeDiscordIpcTransport { Name = "app/com.discordapp.Discord/discord-ipc-0" });
		using var client = new DiscordRpcClient(endpoints.CreateTransport, _probeTimeout);
		var disconnected = false;
		client.Disconnected += (_, _) => disconnected = true;

		var ready = await client.ConnectAsync("1", CancellationToken.None);
		await client.SendCommandAsync("AUTHORIZE");

		var (rejected, discord) = (endpoints.Opened[0], endpoints.Opened[^1]);
		Assert.Multiple(() =>
		{
			Assert.That(client.IsConnected, Is.True);
			Assert.That(DiscordStateMapper.ReadUser(ready).Id, Is.EqualTo("1"));
			Assert.That(discord.Commands.Select(c => c.Command), Does.Contain("AUTHORIZE"));
			Assert.That(rejected.Commands.Select(c => c.Command), Does.Not.Contain("AUTHORIZE"));
			Assert.That(rejected.Disposed, Is.True);
			Assert.That(disconnected, Is.False);
		});
	}

	[Test]
	public async Task A_server_rejecting_commands_as_unknown_is_passed_over_for_discord()
	{
		var endpoints = new FakeDiscordIpcEndpoints(
			() => new FakeDiscordIpcTransport
			{
				Name = "discord-ipc-0",
				Responder = command => FakeResponse.Error(1000, $"Unknown command: {command.Command}")
			},
			() => new FakeDiscordIpcTransport { Name = "discord-ipc-1" });
		using var client = new DiscordRpcClient(endpoints.CreateTransport, _probeTimeout);

		await client.ConnectAsync("1", CancellationToken.None);
		var response = await client.SendCommandAsync("GET_VOICE_SETTINGS");

		Assert.Multiple(() =>
		{
			Assert.That(response.ValueKind, Is.EqualTo(JsonValueKind.Object));
			Assert.That(endpoints.Opened[^1].Name, Is.EqualTo("discord-ipc-1"));
		});
	}

	[Test]
	public void Finding_only_rich_presence_servers_reports_that_discord_itself_is_missing()
	{
		var endpoints = new FakeDiscordIpcEndpoints(
			() => new FakeDiscordIpcTransport { Name = "discord-ipc-0", ReadyData = ArRpcReady },
			() => new FakeDiscordIpcTransport
			{
				Name = "discord-ipc-1",
				Responder = command => FakeResponse.Error(1000, $"Unknown command: {command.Command}")
			});
		using var client = new DiscordRpcClient(endpoints.CreateTransport, _probeTimeout);

		var exception = Assert.ThrowsAsync<DiscordIpcUnavailableException>(async () =>
			await client.ConnectAsync("1", CancellationToken.None));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.RichPresenceOnly, Is.True);
			Assert.That(exception.AccessDenied, Is.False);
			Assert.That(client.IsConnected, Is.False);
			Assert.That(endpoints.Opened, Has.All.Property(nameof(FakeDiscordIpcTransport.Disposed)).True);
		});
	}

	[Test]
	public void A_discord_refusing_access_is_reported_even_behind_a_rich_presence_server()
	{
		var endpoints = new FakeDiscordIpcEndpoints(
			() => new FakeDiscordIpcTransport { Name = "discord-ipc-0", ReadyData = ArRpcReady },
			() => new FakeDiscordIpcTransport { Name = "discord-ipc-1", AccessDenied = true });
		using var client = new DiscordRpcClient(endpoints.CreateTransport, _probeTimeout);

		var exception = Assert.ThrowsAsync<DiscordIpcUnavailableException>(async () =>
			await client.ConnectAsync("1", CancellationToken.None));

		Assert.That(exception!.AccessDenied, Is.True);
	}

	[Test]
	public async Task A_discord_client_that_does_not_answer_the_check_is_still_used()
	{
		var endpoints = new FakeDiscordIpcEndpoints(() => new FakeDiscordIpcTransport
		{
			Name = "discord-ipc-0",
			Responder = command => command.Command == "GET_GUILDS" ? null : FakeResponse.Ok()
		});
		using var client = new DiscordRpcClient(endpoints.CreateTransport, _probeTimeout);

		await client.ConnectAsync("1", CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(client.IsConnected, Is.True);
			Assert.That(endpoints.Opened, Has.Count.EqualTo(1));
		});
	}

	[Test]
	public async Task Cancelling_while_an_endpoint_is_checked_closes_that_endpoint()
	{
		using var cancellation = new CancellationTokenSource();
		var endpoints = new FakeDiscordIpcEndpoints(() => new FakeDiscordIpcTransport
		{
			Name = "discord-ipc-0",
			Responder = command =>
			{
				if (command.Command == "GET_GUILDS")
				{
					cancellation.Cancel();
				}

				return null;
			}
		});
		using var client = new DiscordRpcClient(endpoints.CreateTransport, TimeSpan.FromSeconds(30));

		Assert.CatchAsync<OperationCanceledException>(async () =>
			await client.ConnectAsync("1", cancellation.Token));

		await Task.Yield();
		Assert.Multiple(() =>
		{
			Assert.That(client.IsConnected, Is.False);
			Assert.That(endpoints.Opened.Single().Disposed, Is.True);
		});
	}

	[Test]
	public async Task Disposing_after_passing_over_a_server_closes_the_discord_connection()
	{
		var endpoints = new FakeDiscordIpcEndpoints(
			() => new FakeDiscordIpcTransport { Name = "discord-ipc-0", ReadyData = ArRpcReady },
			() => new FakeDiscordIpcTransport { Name = "discord-ipc-1" });
		var client = new DiscordRpcClient(endpoints.CreateTransport, _probeTimeout);
		await client.ConnectAsync("1", CancellationToken.None);

		client.Dispose();

		Assert.Multiple(() =>
		{
			Assert.That(client.IsConnected, Is.False);
			Assert.That(endpoints.Opened, Has.All.Property(nameof(FakeDiscordIpcTransport.Disposed)).True);
		});
	}
}
