using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

public partial class PluginWebSocketEndpointDispatchTests
{
	private static async Task SendTextAsync(WebSocket plugin, string text)
	{
		await plugin.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, Timeout());
	}

	private static async Task<ProtocolEnvelope> ReceiveAnswerAsync(WebSocket plugin, string correlationId)
	{
		while (await ReceiveEnvelopeAsync(plugin) is { } envelope)
		{
			if (envelope.CorrelationId == correlationId
				|| envelope is { Type: MessageTypes.ProtocolError, CorrelationId: null })
			{
				return envelope;
			}
		}

		throw new AssertionException($"The socket closed before an answer to '{correlationId}' arrived.");
	}

	private static string Ping(string id)
	{
		return $$"""{"type":"session.ping","id":"{{id}}"}""";
	}

	private static string EventPublish(string id, string parameters)
	{
		return $$$"""
				  {"type":"event.publish","id":"{{{id}}}",
				   "payload":{"eventId":"tick","parameters":{{{parameters}}}}}
				  """;
	}

	[Test]
	public async Task A_message_over_the_size_limit_is_a_malformed_envelope_even_when_its_head_is_valid()
	{
		var (endpoint, _) = CreateSocketEndpoint(new ManualTimeProvider());
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var running = endpoint.RunConnectionAsync(pair.Host, ClosePluginId, CloseSessionId);
		await SendHelloAsync(pair.Plugin);
		Assert.That((await ReceiveEnvelopeAsync(pair.Plugin))?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		var head = Ping("oversized");
		var oversized = head + new string(' ', ProtocolLimits.MaxMessageBytes - head.Length) + "garbage";
		await SendTextAsync(pair.Plugin, oversized);
		var answer = await ReceiveAnswerAsync(pair.Plugin, "oversized");

		await SendTextAsync(pair.Plugin, Ping("after"));
		var pong = await ReceiveAnswerAsync(pair.Plugin, "after");

		Assert.Multiple(() =>
		{
			Assert.That(answer.Type, Is.EqualTo(MessageTypes.ProtocolError), "the head must not be processed");
			Assert.That(answer.Error?.Code, Is.EqualTo(ProtocolErrorCodes.MalformedEnvelope));
			Assert.That(pong.Type, Is.EqualTo(MessageTypes.SessionPong), "the session stays open");
		});

		await pair.Plugin.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Timeout());
		await running.WaitAsync(_closeTestTimeout);
	}

	[Test]
	public async Task A_queued_message_the_host_fails_to_handle_is_reported_and_does_not_stop_later_ones()
	{
		var events = new FirstPublishFailsEventBus();
		var (endpoint, _) = CreateSocketEndpoint(new ManualTimeProvider(), eventBus: events);
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var running = endpoint.RunConnectionAsync(pair.Host, ClosePluginId, CloseSessionId);
		await SendHelloAsync(pair.Plugin);
		Assert.That((await ReceiveEnvelopeAsync(pair.Plugin))?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		await SendTextAsync(pair.Plugin, EventPublish("first", """{"value":1}"""));
		var failure = await ReceiveAnswerAsync(pair.Plugin, "first");

		await SendTextAsync(pair.Plugin, EventPublish("second", """{"value":2}"""));
		var occurrence = await events.Delivered.Reader.ReadAsync(Timeout());

		Assert.Multiple(() =>
		{
			Assert.That(failure.Type, Is.EqualTo(MessageTypes.ProtocolError));
			Assert.That(failure.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InternalError));
			Assert.That(occurrence.Parameters["value"], Is.EqualTo(2L), "the dispatch lane still runs");
		});

		await pair.Plugin.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Timeout());
		await running.WaitAsync(_closeTestTimeout);
	}

	private sealed class FirstPublishFailsEventBus : IEventBus
	{
		private int _published;

		public Channel<EventOccurrence> Delivered { get; } = Channel.CreateUnbounded<EventOccurrence>();

		public ChannelReader<EventOccurrence> Reader => Delivered.Reader;

		public void Publish(EventOccurrence occurrence)
		{
			if (Interlocked.Increment(ref _published) == 1)
			{
				throw new InvalidOperationException("The event bus failed.");
			}

			Delivered.Writer.TryWrite(occurrence);
		}
	}
}
