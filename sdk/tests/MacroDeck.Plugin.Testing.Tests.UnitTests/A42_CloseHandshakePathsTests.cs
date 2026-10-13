using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol;
using MacroDeck.Plugin.Protocol.Auth;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

/// <summary>
/// A42 - the test host completes the close handshake on every path a plugin can reach it from, so a
/// plugin always reads a close status instead of a dropped connection: a handshake that fails is
/// answered like the real host does, and a close the plugin starts is echoed.
/// </summary>
[TestFixture]
public class A42_CloseHandshakePathsTests
{
	private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(10);

	[Test]
	public async Task A_first_message_that_is_not_session_hello_gets_a_correlated_protocol_error_and_a_normal_close()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		using var socket = await ConnectRawAsync(host);
		using var deadline = new CancellationTokenSource(Deadline);

		var ping = new ProtocolEnvelope { Type = MessageTypes.SessionPing, Id = Guid.NewGuid().ToString() };
		await socket.SendAsync(ProtocolEnvelopeWriter.WriteToUtf8Bytes(ping), WebSocketMessageType.Text, true, deadline.Token);

		var rejection = await ReadRejectionAsync(socket, deadline.Token);

		Assert.Multiple(() =>
		{
			AssertRejected(rejection, socket);
			Assert.That(rejection.Error?.CorrelationId, Is.EqualTo(ping.Id));
			Assert.That(host.Messages.Closes.Select(close => close.CloseCode),
				Is.EqualTo(new[] { (int)WebSocketCloseStatus.NormalClosure }));
		});
	}

	[Test]
	public async Task A_first_message_that_is_not_json_gets_a_protocol_error_and_a_normal_close()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		using var socket = await ConnectRawAsync(host);
		using var deadline = new CancellationTokenSource(Deadline);

		await socket.SendAsync(Encoding.UTF8.GetBytes("not json"), WebSocketMessageType.Text, true, deadline.Token);

		var rejection = await ReadRejectionAsync(socket, deadline.Token);

		Assert.Multiple(() =>
		{
			AssertRejected(rejection, socket);
			Assert.That(rejection.Error?.CorrelationId, Is.Null);
		});
	}

	[Test]
	public async Task A_session_hello_with_a_malformed_payload_gets_a_protocol_error_and_a_normal_close()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		using var socket = await ConnectRawAsync(host);
		using var deadline = new CancellationTokenSource(Deadline);

		var hello = new ProtocolEnvelope
		{
			Type = MessageTypes.SessionHello,
			Id = Guid.NewGuid().ToString(),
			Payload = JsonSerializer.SerializeToElement(new { protocolVersion = "not a number" })
		};
		await socket.SendAsync(ProtocolEnvelopeWriter.WriteToUtf8Bytes(hello), WebSocketMessageType.Text, true, deadline.Token);

		var rejection = await ReadRejectionAsync(socket, deadline.Token);

		Assert.Multiple(() =>
		{
			AssertRejected(rejection, socket);
			Assert.That(rejection.Error?.CorrelationId, Is.EqualTo(hello.Id));
		});
	}

	[Test]
	public async Task A_plugin_that_sends_nothing_before_the_handshake_deadline_gets_a_protocol_error_and_a_normal_close()
	{
		await using var host = await MacroDeckTestHost.StartAsync(new MacroDeckTestHostOptions
		{
			Timeouts = ProtocolDescriptorFactory.CreateTimeoutsDescriptor() with { Handshake = TimeSpan.FromMilliseconds(300) }
		});
		using var socket = await ConnectRawAsync(host);
		using var deadline = new CancellationTokenSource(Deadline);

		var rejection = await ReadRejectionAsync(socket, deadline.Token);

		AssertRejected(rejection, socket);
	}

	[Test]
	public async Task An_oversized_first_message_gets_a_protocol_error_and_a_normal_close()
	{
		await using var host = await MacroDeckTestHost.StartAsync(SmallMessageLimit());
		using var socket = await ConnectRawAsync(host);
		using var deadline = new CancellationTokenSource(Deadline);

		await socket.SendAsync(new byte[64 * 1024], WebSocketMessageType.Text, true, deadline.Token);

		var rejection = await ReadRejectionAsync(socket, deadline.Token);

		AssertRejected(rejection, socket);
	}

	[Test]
	public async Task A_close_the_plugin_starts_is_echoed_and_completes_the_handshake()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		using var socket = await ConnectRawAsync(host);
		using var deadline = new CancellationTokenSource(Deadline);

		await socket.SendAsync(Envelope(MessageTypes.SessionHello), WebSocketMessageType.Text, true, deadline.Token);
		await host.WaitForSessionAsync();

		await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Plugin stopping.", deadline.Token);

		Assert.Multiple(() =>
		{
			Assert.That(socket.State, Is.EqualTo(WebSocketState.Closed));
			Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.NormalClosure));
			Assert.That(host.Messages.Closes, Is.Empty);
		});
	}

	private sealed record Rejection(ProtocolEnvelope? Error, WebSocketMessageType Next);

	private static async Task<Rejection> ReadRejectionAsync(ClientWebSocket socket, CancellationToken cancellationToken)
	{
		var buffer = new byte[64 * 1024];
		var first = await socket.ReceiveAsync(buffer, cancellationToken);
		var error = first.MessageType == WebSocketMessageType.Close
			? null
			: ProtocolEnvelopeReader.Read(buffer.AsSpan(0, first.Count).ToArray()).Envelope;

		var next = first.MessageType == WebSocketMessageType.Close
			? first.MessageType
			: (await socket.ReceiveAsync(buffer, cancellationToken)).MessageType;

		await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, cancellationToken);

		return new Rejection(error, next);
	}

	private static void AssertRejected(Rejection rejection, ClientWebSocket socket)
	{
		Assert.That(rejection.Error?.Type, Is.EqualTo(MessageTypes.ProtocolError));
		Assert.That(rejection.Error?.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));
		Assert.That(rejection.Next, Is.EqualTo(WebSocketMessageType.Close));
		Assert.That(socket.CloseStatus, Is.EqualTo(WebSocketCloseStatus.NormalClosure));
		Assert.That(socket.State, Is.EqualTo(WebSocketState.Closed));
	}

	private static MacroDeckTestHostOptions SmallMessageLimit()
		=> new() { Limits = ProtocolDescriptorFactory.CreateLimitsDescriptor() with { MaxMessageBytes = 4096 } };

	private static async Task<ClientWebSocket> ConnectRawAsync(MacroDeckTestHost host)
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
		request.Headers.Add(PluginAuthDefaults.PluginIdHeaderName, "test.a42.raw");
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

		return socket;
	}

	private static byte[] Envelope(string type)
		=> ProtocolEnvelopeWriter.WriteToUtf8Bytes(new ProtocolEnvelope { Type = type, Id = Guid.NewGuid().ToString() });
}
