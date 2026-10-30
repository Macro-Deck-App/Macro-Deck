using System.Globalization;
using System.Net.WebSockets;
using System.Text.Json;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Ui;

// Every deck shows the value a variable ends on, however fast it changes and however slow another deck
// reads, and a deck that was away catches up the moment it is back.
[NonParallelizable]
public class UiVariableSyncTests
{
	private const int Updates = 1000;

	private InProcessHost _host = null!;

	[SetUp]
	public async Task SetUp()
	{
		_host = await InProcessHost.StartAsync(null, [typeof(VariableBroadcastBackgroundService)]);
	}

	[TearDown]
	public async Task TearDown()
	{
		await _host.DisposeAsync();
	}

	[Test]
	public async Task Readers_reach_the_final_value_while_another_deck_does_not_read()
	{
		await using var stalled = await ConnectStalledAsync();
		await using var first = await _host.ConnectUiAsync(null);
		await using var second = await _host.ConnectUiAsync(null);
		var id = await CreateVariable("counter");

		for (var value = 1; value <= Updates; value++)
		{
			await SetValue(id, value.ToString(CultureInfo.InvariantCulture));
		}

		var expected = Updates.ToString(CultureInfo.InvariantCulture);
		await first.WaitForAsync(message => Carries(message, "counter", expected), TimeSpan.FromSeconds(30));
		await second.WaitForAsync(message => Carries(message, "counter", expected), TimeSpan.FromSeconds(30));
	}

	[Test]
	public async Task A_deck_that_reconnects_sees_what_changed_while_it_was_away()
	{
		var id = await CreateVariable("scene");
		await SetValue(id, "before");
		var away = await _host.ConnectUiAsync(null);
		await away.DisposeAsync();

		await SetValue(id, "changed while away");
		await using var back = await _host.ConnectUiAsync(null);
		var snapshot = await back.RequestAsync("WatchVariables", (object)new[] { "scene" });

		Assert.That(Carries(snapshot, "scene", "changed while away"), Is.True, snapshot.Payload?.ToString());
	}

	private async Task<UiWsTestClient> ConnectStalledAsync()
	{
		var client = await _host.ConnectUiAsync(null, false, false);
		await client.SendRawAsync(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
			"hello",
			null,
			"hello-1",
			null,
			null,
			null));
		var welcome = await client.Socket.ReceiveAsync(new byte[4096], CancellationToken.None);
		Assert.That(welcome.MessageType, Is.EqualTo(WebSocketMessageType.Text));
		return client;
	}

	private async Task<string> CreateVariable(string name)
	{
		var created = await InProcessHost.ReadJsonAsync(await _host.SendAsync(HttpMethod.Post,
			"/api/variables",
			new { name, type = "text", initialValue = "0" },
			loopback: true));
		Assert.That(created.GetProperty("success").GetBoolean(), Is.True, created.ToString());
		return created.GetProperty("variable").GetProperty("id").GetString()!;
	}

	private async Task SetValue(string id, string value)
	{
		var response = await _host.SendAsync(HttpMethod.Patch, $"/api/variables/{id}/value", new { value }, loopback: true);
		Assert.That(response.IsSuccessStatusCode, Is.True);
	}

	private static bool Carries(UiWsMessage message, string name, string value)
	{
		return message.Payload is { ValueKind: JsonValueKind.Object } payload
			&& payload.TryGetProperty("upserted", out var upserted)
			&& upserted.EnumerateArray()
				.Any(variable => variable.GetProperty("name").GetString() == name
					&& variable.GetProperty("value").GetString() == value);
	}
}
