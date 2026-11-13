using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Logging;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeckHost.Application.Plugins.Logging;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Tests.UnitTests.Auth;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

public partial class PluginWebSocketEndpointDispatchTests
{
	private static async Task SendTextAsync(WebSocket plugin, string text)
	{
		await plugin.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, Timeout());
	}

	private static async Task SendLogPublishAsync(WebSocket plugin, string id, string message)
	{
		var envelope = new ProtocolEnvelope
		{
			Type = MessageTypes.LogPublish,
			Id = id,
			Payload = JsonSerializer.SerializeToElement(
				new LogPublishPayload { Events = [NewLogEvent(message)] },
				PluginProtocolJson.Options)
		};
		await plugin.SendAsync(ProtocolEnvelopeWriter.WriteToUtf8Bytes(envelope), WebSocketMessageType.Text, true, Timeout());
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
			Assert.That(failure.CorrelationId, Is.EqualTo("first"));
			Assert.That(occurrence.Parameters["value"], Is.EqualTo(2L), "the dispatch lane still runs");
		});

		await pair.Plugin.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Timeout());
		await running.WaitAsync(_closeTestTimeout);
	}

	[Test]
	public async Task A_failed_log_publish_does_not_stop_later_batches()
	{
		var ingestor = new FirstIngestFailsLogIngestor();
		var (endpoint, _) = CreateSocketEndpoint(new ManualTimeProvider(), logIngestor: ingestor);
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var running = endpoint.RunConnectionAsync(pair.Host, ClosePluginId, CloseSessionId);
		await SendHelloAsync(pair.Plugin);
		Assert.That((await ReceiveEnvelopeAsync(pair.Plugin))?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		await SendLogPublishAsync(pair.Plugin, "first", "first batch");
		await ingestor.FirstFailure.Task.WaitAsync(Timeout());
		await SendLogPublishAsync(pair.Plugin, "second", "second batch");

		using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(2));
		var ingested = await ingestor.Ingested.Reader.ReadAsync(timeout.Token);

		Assert.That(ingested, Is.EqualTo("second batch"), "the log lane must continue after a failed batch");

		await pair.Plugin.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Timeout());
		await running.WaitAsync(_closeTestTimeout);
	}

	[TestCase("""{"value":1e400}""")]
	[TestCase("""{"value":-1e400}""")]
	[TestCase("""{"label":"x","reading":{"value":1e400}}""")]
	[TestCase("""{"readings":[1.5,[-1e400]]}""")]
	public async Task An_event_publish_with_a_number_beyond_double_range_is_rejected_and_not_published(
		string parameters)
	{
		var events = new FakeEventBus();
		var (endpoint, _) = CreateSocketEndpoint(new ManualTimeProvider(), eventBus: events);
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();
		var running = endpoint.RunConnectionAsync(pair.Host, ClosePluginId, CloseSessionId);
		await SendHelloAsync(pair.Plugin);
		Assert.That((await ReceiveEnvelopeAsync(pair.Plugin))?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		await SendTextAsync(pair.Plugin, EventPublish("non-finite", parameters));
		var rejection = await ReceiveAnswerAsync(pair.Plugin, "non-finite");

		await SendTextAsync(pair.Plugin, EventPublish("finite", """{"value":1.5}"""));
		var occurrence = await events.Reader.ReadAsync(Timeout());

		Assert.Multiple(() =>
		{
			Assert.That(rejection.Type, Is.EqualTo(MessageTypes.ProtocolError));
			Assert.That(rejection.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
			Assert.That(rejection.CorrelationId, Is.EqualTo("non-finite"));
			Assert.That(occurrence.Parameters["value"], Is.EqualTo(1.5d), "the rejected event was published");
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

	private sealed class FirstIngestFailsLogIngestor : IPluginLogIngestor
	{
		private int _ingested;

		public TaskCompletionSource FirstFailure { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public Channel<string> Ingested { get; } = Channel.CreateUnbounded<string>();

		public PluginLogIngestResult Ingest(string pluginId, string sessionId, IReadOnlyList<LogEventDto> events)
		{
			if (Interlocked.Increment(ref _ingested) == 1)
			{
				FirstFailure.TrySetResult();
				throw new InvalidOperationException("The log sink failed.");
			}

			Ingested.Writer.TryWrite(events[0].MessageTemplate);
			return new PluginLogIngestResult
			{
				Outcome = PluginLogIngestOutcome.Ingested,
				IngestedCount = events.Count
			};
		}

		public void Evict(string pluginId)
		{
		}
	}
}
