using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeck.Plugin.Testing.Tests.UnitTests.Support;
using MacroDeck.Sdk.Events;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A41 - a close the test host sends completes its handshake even while the plugin is still sending, so
/// the plugin reads the close code instead of a dropped connection and a managed plugin stops on
/// SupervisorShutdown (4004) instead of reconnecting.
/// </summary>
[TestFixture]
public class A41CloseHandshakeTests
{
	[Test]
	public async Task A_peer_still_sending_after_a_4004_close_completes_the_close_handshake()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		using var socket = await ConnectRawSessionAsync(host);
		await host.WaitForSessionAsync();

		await host.DisconnectAsync(ProtocolCloseCodes.SupervisorShutdown);

		var buffer = new byte[64 * 1024];
		WebSocketReceiveResult received;
		do
		{
			received = await socket.ReceiveAsync(buffer, CancellationToken.None);
		} while (received.MessageType != WebSocketMessageType.Close);

		for (var i = 0; i < 10; i++)
		{
			await socket.SendAsync(Envelope(MessageTypes.SessionPing),
				WebSocketMessageType.Text,
				true,
				CancellationToken.None);
			await Task.Delay(TimeSpan.FromMilliseconds(30));
		}

		await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(socket.CloseStatus, Is.EqualTo((WebSocketCloseStatus)ProtocolCloseCodes.SupervisorShutdown));
			Assert.That(socket.State, Is.EqualTo(WebSocketState.Closed));
		});
	}

	// Only Windows resets the connection hard enough to lose the close frame, so this fails there alone.
	[Test]
	public async Task A_managed_plugin_that_is_sending_when_the_host_closes_with_4004_stops_without_reconnecting()
	{
		var integration = new TestIntegration("test.a41")
			.WithEvent(new EventDefinition { Id = "tick", Name = "Tick" });

		var builder = MacroDeckPlugin.CreatePlugin().RegisterIntegration(_ => integration);

		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.HostAsync(builder, PluginTestCredentials.Managed);
		await host.WaitForSessionAsync();

		var payload = new string('x', 16 * 1024);
		using var stopSending = new CancellationTokenSource();
		var sending = Task.Run(async () =>
		{
			while (!stopSending.IsCancellationRequested)
			{
				if (integration.Context is not { } context)
				{
					await Task.Delay(10);
					continue;
				}

				context.Events.Publish("tick", new Dictionary<string, object?> { ["payload"] = payload });
				await Task.Delay(TimeSpan.FromMilliseconds(2));
			}
		});

		try
		{
			await host.Events.WaitForAsync("tick", TimeSpan.FromSeconds(5));
			await host.DisconnectAsync(ProtocolCloseCodes.SupervisorShutdown);

			var report = await HealthPolling.WaitUntilAsync(plugin, StoppedOnTheClose, TimeSpan.FromSeconds(15));
			var hellos = host.Messages.All.Count(message => message.Envelope.Type == MessageTypes.SessionHello);

			Assert.Multiple(() =>
			{
				Assert.That(StoppedOnTheClose(report),
					Is.True,
					$"a managed plugin stops once it observes SupervisorShutdown (4004), but its connection is {report.Status}");
				Assert.That(hellos, Is.EqualTo(1), "the plugin must not open a second session after the 4004 close");
			});
		}
		finally
		{
			await stopSending.CancelAsync();
			await sending;
		}
	}

	// An in-process subject's host keeps serving after StopApplication, so a faulted connection is the
	// stop signal there; an out-of-process one goes away entirely.
	private static bool StoppedOnTheClose(PluginHealthReport health)
	{
		return !health.Live || health.Status == "Faulted";
	}

	private static async Task<ClientWebSocket> ConnectRawSessionAsync(MacroDeckTestHost host)
	{
		using var http = new HttpClient { BaseAddress = new Uri(host.Url) };
		using var request = new HttpRequestMessage(HttpMethod.Post, ProtocolConstants.SessionsPath)
		{
			Content = JsonContent.Create(new PluginSessionRequest
				{
					RequestedVersion = new ProtocolVersionRange
					{
						Minimum = ProtocolVersions.Supported.Min(), Maximum = ProtocolVersions.Supported.Max()
					},
					Capabilities = []
				},
				options: PluginProtocolJson.Options)
		};
		request.Headers.Add(PluginAuthDefaults.PluginIdHeaderName, "test.a41.raw");
		request.Headers.Add(PluginAuthDefaults.PluginSecretHeaderName, "secret");

		using var response = await http.SendAsync(request);
		response.EnsureSuccessStatusCode();
		var session = await response.Content.ReadFromJsonAsync<PluginSessionResponse>(PluginProtocolJson.Options);

		var socket = new ClientWebSocket();
		socket.Options.AddSubProtocol(ProtocolConstants.WebSocketSubProtocol);
		socket.Options.SetRequestHeader("Authorization",
			new AuthenticationHeaderValue(PluginAuthDefaults.BearerScheme, session!.SessionToken).ToString());

		var webSocketUri = new UriBuilder(host.Url) { Scheme = "ws", Path = ProtocolConstants.WebSocketPath }.Uri;
		await socket.ConnectAsync(webSocketUri, CancellationToken.None);
		await socket.SendAsync(Hello(session.SessionId), WebSocketMessageType.Text, true, CancellationToken.None);

		return socket;
	}

	private static byte[] Hello(string sessionId)
	{
		return ProtocolEnvelopeWriter.WriteToUtf8Bytes(new ProtocolEnvelope
		{
			Type = MessageTypes.SessionHello,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new SessionHelloPayload
					{ ProtocolVersion = ProtocolVersions.Supported.Max(), SessionId = sessionId },
				PluginProtocolJson.Options)
		});
	}

	private static byte[] Envelope(string type)
	{
		return ProtocolEnvelopeWriter.WriteToUtf8Bytes(new ProtocolEnvelope
			{ Type = type, Id = Guid.NewGuid().ToString() });
	}
}
