using System.Net.WebSockets;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Auth;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Assets;
using MacroDeckHost.Application.Plugins.Runtime;
using MacroDeckHost.Plugins;
using MacroDeckHost.Plugins.Capabilities.Callbacks;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Plugins;

public partial class PluginWebSocketEndpointDispatchTests
{
	private const string ClosePluginId = "com.example.close";
	private const string CloseSessionId = "close-session";

	private static readonly TimeSpan _closeTestTimeout = TimeSpan.FromSeconds(15);

	[Test]
	public async Task A_handshake_timeout_reaches_the_plugin_as_invalid_payload_and_a_normal_close()
	{
		var clock = new ManualTimeProvider();
		var (endpoint, _) = CreateSocketEndpoint(clock);
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();

		var running = endpoint.RunConnectionAsync(pair.Host, ClosePluginId, CloseSessionId);
		await clock.WaitForScheduleAsync(0).WaitAsync(_closeTestTimeout);
		clock.Advance(ProtocolTimeouts.Handshake);

		var error = await ReceiveEnvelopeAsync(pair.Plugin);
		Assert.That(error?.Type, Is.EqualTo(MessageTypes.ProtocolError));
		Assert.That(error?.Error?.Code, Is.EqualTo(ProtocolErrorCodes.InvalidPayload));

		Assert.That(await ReceiveEnvelopeAsync(pair.Plugin), Is.Null, "expected the host's close frame");
		Assert.That(pair.Plugin.CloseStatus, Is.EqualTo(WebSocketCloseStatus.NormalClosure));
		Assert.That(pair.Plugin.CloseStatusDescription, Is.EqualTo("Handshake timed out."));

		await pair.Plugin.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Timeout());
		Assert.That(pair.Plugin.State, Is.EqualTo(WebSocketState.Closed));
		await running.WaitAsync(_closeTestTimeout);
	}

	[Test]
	public async Task A_plugin_that_never_answers_the_timeout_close_is_dropped_after_the_graceful_close_budget()
	{
		var clock = new ManualTimeProvider();
		var (endpoint, _) = CreateSocketEndpoint(clock);
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();

		var running = endpoint.RunConnectionAsync(pair.Host, ClosePluginId, CloseSessionId);
		await clock.WaitForScheduleAsync(0).WaitAsync(_closeTestTimeout);
		clock.Advance(ProtocolTimeouts.Handshake);

		await running.WaitAsync(ProtocolTimeouts.GracefulClose + _closeTestTimeout);
		Assert.That(pair.Host.State, Is.EqualTo(WebSocketState.Aborted));
	}

	[Test]
	public async Task A_close_before_session_hello_is_answered_with_the_plugins_close()
	{
		var (endpoint, _) = CreateSocketEndpoint(new ManualTimeProvider());
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();

		var running = endpoint.RunConnectionAsync(pair.Host, ClosePluginId, CloseSessionId);
		await pair.Plugin.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bye.", Timeout());

		Assert.That(pair.Plugin.State, Is.EqualTo(WebSocketState.Closed));
		Assert.That(pair.Plugin.CloseStatus, Is.EqualTo(WebSocketCloseStatus.NormalClosure));
		await running.WaitAsync(_closeTestTimeout);
	}

	[Test]
	public async Task A_close_after_session_welcome_is_echoed_and_leaves_the_session_resumable()
	{
		var (endpoint, registry) = CreateSocketEndpoint(new ManualTimeProvider());
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();

		var running = endpoint.RunConnectionAsync(pair.Host, ClosePluginId, CloseSessionId);
		await SendHelloAsync(pair.Plugin);
		Assert.That((await ReceiveEnvelopeAsync(pair.Plugin))?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		await pair.Plugin.CloseAsync((WebSocketCloseStatus)4321, "Plugin stopping.", Timeout());

		Assert.That(pair.Plugin.State, Is.EqualTo(WebSocketState.Closed));
		Assert.That((int?)pair.Plugin.CloseStatus, Is.EqualTo(4321));
		Assert.That(pair.Plugin.CloseStatusDescription, Is.EqualTo("Plugin stopping."));

		await running.WaitAsync(_closeTestTimeout);
		var session = registry.Snapshot().Single(record => record.SessionId == CloseSessionId);
		Assert.That(session.State, Is.EqualTo(PluginSessionState.Dropped));
	}

	[Test]
	public async Task A_plugin_connected_when_the_host_starts_stopping_still_gets_the_supervisors_goodbye_and_close()
	{
		using var lifetime = new StoppingLifetime();
		using var connections = new PluginConnectionLifetime();
		var (endpoint, registry) = CreateSocketEndpoint(new ManualTimeProvider(), lifetime, connections);
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();

		var running = endpoint.RunConnectionAsync(pair.Host, ClosePluginId, CloseSessionId);
		await SendHelloAsync(pair.Plugin);
		Assert.That((await ReceiveEnvelopeAsync(pair.Plugin))?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		lifetime.StopApplication();
		await registry.SendToPlugin(ClosePluginId,
			new ProtocolEnvelope { Type = MessageTypes.SessionGoodbye, Id = Guid.CreateVersion7().ToString() },
			CancellationToken.None);
		await registry.TerminateForPlugin(ClosePluginId, ProtocolCloseCodes.SupervisorShutdown, "Stopping.");

		var received = new List<string>();
		while (await ReceiveEnvelopeAsync(pair.Plugin) is { } envelope)
		{
			received.Add(envelope.Type);
		}

		Assert.That(received, Does.Contain(MessageTypes.SessionGoodbye));
		Assert.That((int?)pair.Plugin.CloseStatus, Is.EqualTo(ProtocolCloseCodes.SupervisorShutdown));

		await pair.Plugin.CloseAsync(WebSocketCloseStatus.NormalClosure, null, Timeout());
		await running.WaitAsync(_closeTestTimeout);
	}

	[Test]
	public async Task A_plugin_socket_still_open_is_ended_once_plugin_connections_close()
	{
		using var lifetime = new StoppingLifetime();
		using var connections = new PluginConnectionLifetime();
		var (endpoint, _) = CreateSocketEndpoint(new ManualTimeProvider(), lifetime, connections);
		using var pair = await PluginWebSocketConnectionPair.CreateAsync();

		var running = endpoint.RunConnectionAsync(pair.Host, ClosePluginId, CloseSessionId);
		await SendHelloAsync(pair.Plugin);
		Assert.That((await ReceiveEnvelopeAsync(pair.Plugin))?.Type, Is.EqualTo(MessageTypes.SessionWelcome));

		connections.Close();

		await running.WaitAsync(_closeTestTimeout);
		Assert.That(pair.Host.State, Is.Not.EqualTo(WebSocketState.Open));
	}

	private static (PluginWebSocketEndpoint Endpoint, PluginSessionRegistry Registry) CreateSocketEndpoint(
		TimeProvider clock,
		IHostApplicationLifetime? lifetime = null,
		PluginConnectionLifetime? connectionLifetime = null)
	{
		var registry = new PluginSessionRegistry(clock, Serilog.Core.Logger.None);
		var statePusher
			= new HostStatePusher(registry,
				new EmptyDeckNavigator(),
				new EmptyScriptApi(),
				new EmptyWidgetApi(),
				new StubEventBindingTracker(),
				new MacroDeckHost.Application.Deck.DeckClientTracker(Serilog.Core.Logger.None),
				new Adb.FakeAdbManager(),
				new FixedAdbAccessPolicy(),
				Serilog.Core.Logger.None);
		var endpoint = new PluginWebSocketEndpoint(registry,
			new FakePluginCapabilityInvoker(),
			new FakeRemotePluginIntegrationRegistrar(),
			new FakePluginCallbackRouter(),
			new PluginAssetReceiver(new InMemoryPluginAssetCache()),
			statePusher,
			new FakeEventBus(),
			new LoginThrottle(clock),
			clock,
			new RecordingMediator(),
			lifetime ?? new NeverStoppingLifetime(),
			CreateLogIngestor(registry),
			Serilog.Core.Logger.None,
			connectionLifetime: connectionLifetime);

		registry.Create(new PluginSessionRecord
			{
				SessionId = CloseSessionId,
				PluginId = ClosePluginId,
				DisplayName = "Close Plugin",
				Origin = PluginSessionOrigin.Managed,
				NegotiatedVersion = 1,
				Capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal),
				DeclaredCapabilities = [],
				State = PluginSessionState.Awaiting,
				CreatedAt = clock.GetUtcNow()
			})
			.GetAwaiter()
			.GetResult();

		return (endpoint, registry);
	}

	private static async Task SendHelloAsync(WebSocket plugin)
	{
		var hello = new ProtocolEnvelope
		{
			Type = MessageTypes.SessionHello,
			Id = Guid.CreateVersion7().ToString(),
			Payload = JsonSerializer.SerializeToElement(
				new SessionHelloPayload { ProtocolVersion = 1, SessionId = CloseSessionId },
				PluginProtocolJson.Options)
		};

		await plugin.SendAsync(ProtocolEnvelopeWriter.WriteToUtf8Bytes(hello),
			WebSocketMessageType.Text,
			true,
			Timeout());
	}

	private static async Task<ProtocolEnvelope?> ReceiveEnvelopeAsync(WebSocket plugin)
	{
		var buffer = new byte[ProtocolLimits.MaxMessageBytes];
		var result = await plugin.ReceiveAsync(buffer, Timeout());
		return result.MessageType == WebSocketMessageType.Close
			? null
			: ProtocolEnvelopeReader.Read(buffer.AsSpan(0, result.Count).ToArray()).Envelope;
	}

	private static CancellationToken Timeout()
	{
		return new CancellationTokenSource(_closeTestTimeout).Token;
	}

	private sealed class StoppingLifetime : IHostApplicationLifetime, IDisposable
	{
		private readonly CancellationTokenSource _stopping = new();

		public CancellationToken ApplicationStarted => CancellationToken.None;
		public CancellationToken ApplicationStopping => _stopping.Token;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
			_stopping.Cancel();
		}

		public void Dispose()
		{
			_stopping.Dispose();
		}
	}
}
