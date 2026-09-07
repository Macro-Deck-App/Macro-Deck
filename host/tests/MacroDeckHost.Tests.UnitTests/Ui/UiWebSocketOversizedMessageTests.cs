using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeckHost.Ui;

namespace MacroDeckHost.Tests.UnitTests.Ui;

/// <summary>
/// An outbound envelope over the protocol's size limit used to abort the socket with no error frame and
/// no close reason, so a client waited forever for a message nobody told it had been dropped. Whatever
/// the cause of the oversize, the failure has to reach the client and the log.
/// </summary>
[TestFixture]
public sealed class UiWebSocketOversizedMessageTests
{
	[Test]
	public async Task An_oversized_response_is_reported_to_the_request_that_asked_for_it()
	{
		var socket = new RecordingWebSocket();
		var channel = Channel.CreateUnbounded<UiWebSocketEnvelope>();
		using var cancellation = new CancellationTokenSource();
		channel.Writer.TryWrite(Oversized("response", correlationId: "request-1"));
		channel.Writer.TryComplete();

		await UiWebSocketEndpoint.WriteAsync(socket, channel.Reader, cancellation, TimeProvider.System);

		var sent = socket.Sent.Select(Read).ToList();

		Assert.Multiple(() =>
		{
			Assert.That(sent, Has.Count.EqualTo(1), "the oversized envelope must not be sent, its failure must");
			Assert.That(sent[0].Kind, Is.EqualTo("error"));
			Assert.That(sent[0].CorrelationId,
				Is.EqualTo("request-1"),
				"a client matches an error to its pending request by correlation id, so an uncorrelated one is lost");
			Assert.That(sent[0].Error!.Code, Is.EqualTo(UiWebSocketEndpoint.OversizedCode));
			Assert.That(socket.Aborted, Is.False, "the session must not die over one message it could report");
		});
	}

	[Test]
	public async Task A_session_survives_an_oversized_response_and_keeps_sending()
	{
		var socket = new RecordingWebSocket();
		var channel = Channel.CreateUnbounded<UiWebSocketEnvelope>();
		using var cancellation = new CancellationTokenSource();
		channel.Writer.TryWrite(Oversized("response", correlationId: "request-1"));
		channel.Writer.TryWrite(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
			"message",
			"AfterTheFailure",
			null,
			null,
			"small",
			null));
		channel.Writer.TryComplete();

		await UiWebSocketEndpoint.WriteAsync(socket, channel.Reader, cancellation, TimeProvider.System);

		Assert.Multiple(() =>
		{
			Assert.That(socket.Sent.Select(Read).Select(envelope => envelope.Kind),
				Is.EqualTo(new[] { "error", "message" }),
				"the failure is reported, then the connection carries on with the next message");
			Assert.That(cancellation.IsCancellationRequested, Is.False);
		});
	}

	// A pushed message carries no correlation id, so an error frame would be dropped by the client and the
	// drop would be as silent as the abort was. Closing with a reason is what a client can actually see.
	[Test]
	public async Task An_oversized_pushed_message_closes_the_session_with_a_reason()
	{
		var socket = new RecordingWebSocket();
		var channel = Channel.CreateUnbounded<UiWebSocketEnvelope>();
		using var cancellation = new CancellationTokenSource();
		channel.Writer.TryWrite(Oversized("message", correlationId: null));
		channel.Writer.TryComplete();

		await UiWebSocketEndpoint.WriteAsync(socket, channel.Reader, cancellation, TimeProvider.System);

		Assert.Multiple(() =>
		{
			Assert.That(socket.Sent, Is.Empty);
			Assert.That(socket.ClosedWith, Is.EqualTo(WebSocketCloseStatus.MessageTooBig));
			Assert.That(socket.CloseDescription, Is.Not.Null.And.Not.Empty, "a close with no reason explains nothing");
			Assert.That(socket.Aborted, Is.False, "an abort gives the client no close frame to read a reason from");
			Assert.That(cancellation.IsCancellationRequested, Is.True, "the rest of the connection has to wind down");
		});
	}

	[Test]
	public async Task A_message_within_the_limit_is_still_sent_untouched()
	{
		var socket = new RecordingWebSocket();
		var channel = Channel.CreateUnbounded<UiWebSocketEnvelope>();
		using var cancellation = new CancellationTokenSource();
		channel.Writer.TryWrite(new UiWebSocketEnvelope(UiWebSocketProtocol.Version,
			"message",
			"Notice",
			null,
			null,
			"payload",
			null));
		channel.Writer.TryComplete();

		await UiWebSocketEndpoint.WriteAsync(socket, channel.Reader, cancellation, TimeProvider.System);

		Assert.Multiple(() =>
		{
			Assert.That(socket.Sent.Select(Read).Select(envelope => envelope.Type), Is.EqualTo(new[] { "Notice" }));
			Assert.That(socket.ClosedWith, Is.Null);
			Assert.That(socket.Aborted, Is.False);
		});
	}

	private static UiWebSocketEnvelope Oversized(string kind, string? correlationId)
		=> new(UiWebSocketProtocol.Version,
			kind,
			"Big",
			null,
			correlationId,
			new string('x', UiWebSocketProtocol.MaxMessageBytes + 1),
			null);

	private static UiWebSocketEnvelope Read(byte[] bytes)
		=> JsonSerializer.Deserialize<UiWebSocketEnvelope>(bytes, UiWebSocketProtocol.Json)!;

	private sealed class RecordingWebSocket : WebSocket
	{
		private WebSocketState _state = WebSocketState.Open;

		public List<byte[]> Sent { get; } = [];

		public WebSocketCloseStatus? ClosedWith { get; private set; }

		public string? CloseDescription { get; private set; }

		public bool Aborted { get; private set; }

		public override WebSocketCloseStatus? CloseStatus => ClosedWith;

		public override string? CloseStatusDescription => CloseDescription;

		public override WebSocketState State => _state;

		public override string? SubProtocol => UiWebSocketProtocol.SubProtocol;

		public override void Abort()
		{
			Aborted = true;
			_state = WebSocketState.Aborted;
		}

		public override Task CloseAsync(WebSocketCloseStatus closeStatus,
			string? statusDescription,
			CancellationToken cancellationToken)
			=> CloseOutputAsync(closeStatus, statusDescription, cancellationToken);

		public override Task CloseOutputAsync(WebSocketCloseStatus closeStatus,
			string? statusDescription,
			CancellationToken cancellationToken)
		{
			ClosedWith = closeStatus;
			CloseDescription = statusDescription;
			_state = WebSocketState.CloseSent;
			return Task.CompletedTask;
		}

		public override void Dispose()
		{
		}

		public override Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public override Task SendAsync(ArraySegment<byte> buffer,
			WebSocketMessageType messageType,
			bool endOfMessage,
			CancellationToken cancellationToken)
		{
			Sent.Add(buffer.ToArray());
			return Task.CompletedTask;
		}
	}
}
