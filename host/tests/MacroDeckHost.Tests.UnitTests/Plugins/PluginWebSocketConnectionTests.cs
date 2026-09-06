using System.Net.WebSockets;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Plugins;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

[TestFixture]
public class PluginWebSocketConnectionTests
{
	[Test]
	public async Task Send_Aborts_The_Socket_Instead_Of_Parking_When_The_Peer_Never_Drains()
	{
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var clock = new ManualTimeProvider();
		var connection = new PluginWebSocketConnection(pair.Host, "conn-1", clock);

		// Well past any OS socket buffer, so the underlying SendAsync genuinely blocks - pair.Plugin is
		// deliberately never read from, standing in for a plugin that has stopped draining its own
		// receive buffer.
		var envelope = new ProtocolEnvelope
		{
			Type = MessageTypes.SessionPing,
			Id = "1",
			Payload = JsonSerializer.SerializeToElement(new string('a', 6_000_000))
		};

		var sendTask = connection.Send(envelope);

		// By this point Send has already registered its internal Task.Delay(ProtocolTimeouts.DefaultRequest,
		// clock, ...) timer and suspended on Task.WhenAny - both statements before that await run
		// synchronously, and the real socket send cannot complete synchronously for 6 MB of unread data -
		// so advancing the fake clock here is deterministic and needs no real wait.
		clock.Advance(ProtocolTimeouts.DefaultRequest);

		Assert.ThrowsAsync<OperationCanceledException>(async () => await sendTask);
		Assert.That(pair.Host.State, Is.EqualTo(WebSocketState.Aborted));
	}

	[Test]
	public async Task Send_Delivers_Normally_When_The_Peer_Keeps_Draining()
	{
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var clock = new ManualTimeProvider();
		var connection = new PluginWebSocketConnection(pair.Host, "conn-1", clock);

		var envelope = new ProtocolEnvelope { Type = MessageTypes.SessionPing, Id = "1" };

		var receive = ReceiveOneAsync(pair.Plugin);
		await connection.Send(envelope);
		var received = await receive;

		Assert.Multiple(() =>
		{
			Assert.That(received, Is.EqualTo(MessageTypes.SessionPing));
			Assert.That(pair.Host.State, Is.EqualTo(WebSocketState.Open));
		});
	}

	private static async Task<string> ReceiveOneAsync(WebSocket socket)
	{
		var buffer = new byte[8192];
		using var message = new MemoryStream();

		while (true)
		{
			var result = await socket.ReceiveAsync(buffer, CancellationToken.None);
			message.Write(buffer, 0, result.Count);

			if (result.EndOfMessage)
			{
				var read = ProtocolEnvelopeReader.Read(message.ToArray());
				return read.Envelope!.Type;
			}
		}
	}
}
