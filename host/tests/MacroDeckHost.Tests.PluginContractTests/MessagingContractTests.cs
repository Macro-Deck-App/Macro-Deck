using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Protocol.Versioning;
using MacroDeckHost.Application.Messaging;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Plugins.Capabilities;
using MacroDeckHost.Plugins.Capabilities.Callbacks;

namespace MacroDeckHost.Tests.PluginContractTests;

[TestFixture]
internal sealed class MessagingContractTests
{
	private const string Obs = "com.example.obs";
	private const string Deck = "com.example.deck";
	private const string Legacy = "com.example.legacy";

	private PluginSessionRegistry _sessions = null!;
	private MessageBroker _broker = null!;
	private WirePlugins _plugins = null!;
	private PluginMessagingRegistrations _registrations = null!;
	private PluginMessagingInvokeRunner _runner = null!;

	[SetUp]
	public async Task SetUp()
	{
		_sessions = new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None);
		_broker = new MessageBroker(Serilog.Core.Logger.None);
		_plugins = new WirePlugins();
		_registrations = new PluginMessagingRegistrations(_broker,
			_sessions,
			_plugins,
			new PluginSessionConnectionState(_sessions));
		_runner = new PluginMessagingInvokeRunner(_broker, _registrations, TimeProvider.System, Serilog.Core.Logger.None);

		await Connect(Obs, "session-obs", declaresMessaging: true);
		await Connect(Deck, "session-deck", declaresMessaging: true);
		await Connect(Legacy, "session-legacy", declaresMessaging: false);
	}

	[TearDown]
	public void TearDown()
	{
		_registrations.Dispose();
		_broker.Dispose();
	}

	[Test]
	public async Task A_plugin_built_before_messaging_is_sent_no_deliveries_and_refused_the_new_api()
	{
		await Invoke(Deck, "session-deck", """{"api":"messaging","operation":"subscriptions","arguments":{"events":["obs.*"]}}""");

		var refusal = await Invoke(Legacy, "session-legacy",
			"""{"api":"messaging","operation":"subscriptions","arguments":{"events":["obs.*"]}}""");
		await Invoke(Obs, "session-obs", """{"api":"messaging","operation":"publish","arguments":{"topic":"obs.scene.changed"}}""");
		await _plugins.WaitForAsync(Deck, 1);

		Assert.Multiple(() =>
		{
			Assert.That(refusal.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
			Assert.That(refusal.Error?.Details?["reason"], Is.EqualTo(ProtocolErrorReasons.MessagingNotDeclared));
			Assert.That(_plugins.Received(Legacy), Is.Empty);
		});
	}

	[Test]
	public async Task An_event_one_plugin_publishes_reaches_another_plugins_subscription()
	{
		await Invoke(Deck, "session-deck", """{"api":"messaging","operation":"subscriptions","arguments":{"events":["obs.*"]}}""");

		var accepted = await Invoke(Obs, "session-obs",
			"""{"api":"messaging","operation":"publish","arguments":{"topic":"obs.scene.changed","payload":{"scene":"Live"}}}""");
		await _plugins.WaitForAsync(Deck, 1);

		var delivery = _plugins.Received(Deck).Single();
		Assert.Multiple(() =>
		{
			Assert.That(accepted.Error, Is.Null);
			Assert.That((delivery.Kind, delivery.LocalId, delivery.Operation), Is.EqualTo(("messaging", "provider", "event")));
			Assert.That(delivery.Arguments.GetProperty("topic").GetString(), Is.EqualTo("obs.scene.changed"));
			Assert.That(delivery.Arguments.GetProperty("sender").GetString(), Is.EqualTo(Obs));
			Assert.That(delivery.Arguments.GetProperty("payload").GetProperty("scene").GetString(), Is.EqualTo("Live"));
		});
	}

	[Test]
	public async Task A_request_goes_to_the_plugin_that_handles_the_topic_and_its_reply_comes_back()
	{
		_plugins.Answer(Obs, """{"payload":{"scene":"Live"}}""");
		await Invoke(Obs, "session-obs", """{"api":"messaging","operation":"subscriptions","arguments":{"requests":["obs.scene.current"]}}""");

		var result = await Invoke(Deck, "session-deck", """{"api":"messaging","operation":"request","arguments":{"topic":"obs.scene.current"}}""");

		Assert.Multiple(() =>
		{
			Assert.That(result.Error, Is.Null);
			Assert.That(result.Data?.GetProperty("payload").GetProperty("scene").GetString(), Is.EqualTo("Live"));
			Assert.That(_plugins.Received(Obs).Single().Operation, Is.EqualTo("request"));
		});
	}

	[Test]
	public async Task A_reply_that_is_not_a_reply_object_fails_the_request_instead_of_leaving_it_unanswered()
	{
		_plugins.Answer(Obs, "[1, 2]");
		await Invoke(Obs, "session-obs", """{"api":"messaging","operation":"subscriptions","arguments":{"requests":["obs.scene.current"]}}""");

		var result = await Invoke(Deck, "session-deck", """{"api":"messaging","operation":"request","arguments":{"topic":"obs.scene.current"}}""");

		Assert.That(result.Error?.Details?["reason"], Is.EqualTo(ProtocolErrorReasons.MessagingHandlerFailed));
	}

	[Test]
	public async Task A_second_plugin_claiming_a_handled_topic_is_told_who_handles_it()
	{
		await Invoke(Obs, "session-obs", """{"api":"messaging","operation":"subscriptions","arguments":{"commands":["obs.scene.switch"]}}""");

		var result = await Invoke(Deck, "session-deck",
			"""{"api":"messaging","operation":"subscriptions","arguments":{"commands":["obs.scene.switch"]}}""");

		var rejected = result.Data?.GetProperty("rejected")[0];
		Assert.Multiple(() =>
		{
			Assert.That(rejected?.GetProperty("kind").GetString(), Is.EqualTo("command"));
			Assert.That(rejected?.GetProperty("reason").GetString(), Is.EqualTo(ProtocolErrorReasons.MessagingTopicHandled));
			Assert.That(rejected?.GetProperty("owner").GetString(), Is.EqualTo(Obs));
		});
	}

	[Test]
	public async Task Once_the_handling_plugin_is_gone_a_request_finds_no_handler()
	{
		await Invoke(Obs, "session-obs", """{"api":"messaging","operation":"subscriptions","arguments":{"requests":["obs.scene.current"]}}""");

		_sessions.Detach("session-obs", TimeProvider.System.GetUtcNow() - ProtocolTimeouts.SessionResumeWindow - TimeSpan.FromSeconds(1));
		_ = _sessions.Snapshot();

		var result = await Invoke(Deck, "session-deck", """{"api":"messaging","operation":"request","arguments":{"topic":"obs.scene.current"}}""");

		Assert.That(result.Error?.Details?["reason"], Is.EqualTo(ProtocolErrorReasons.MessagingNoHandler));
	}

	[Test]
	public async Task A_sync_from_a_session_that_was_replaced_changes_nothing()
	{
		await Connect(Deck, "session-deck-2", declaresMessaging: true);

		var result = await Invoke(Deck, "session-deck",
			"""{"api":"messaging","operation":"subscriptions","arguments":{"requests":["deck.page.current"]}}""");

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.SessionNotFound));
			Assert.That(_broker.CurrentRegistrationKey(Deck), Is.Null);
		});
	}

	[Test]
	public async Task An_unknown_operation_is_unsupported_rather_than_fatal()
	{
		var result = await Invoke(Deck, "session-deck", """{"api":"messaging","operation":"broadcast","arguments":{}}""");

		Assert.That(result.Error?.Code, Is.EqualTo(ProtocolErrorCodes.CapabilityUnsupported));
	}

	private async Task Connect(string pluginId, string sessionId, bool declaresMessaging)
	{
		var capabilities = new Dictionary<string, CapabilityNegotiationResult>(StringComparer.Ordinal)
		{
			[CapabilityKinds.Actions] = CapabilityNegotiationResult.Accept(CapabilityKinds.Actions, 1)
		};
		if (declaresMessaging)
		{
			capabilities[CapabilityKinds.Messaging] = CapabilityNegotiationResult.Accept(CapabilityKinds.Messaging, 1);
		}

		await _sessions.Create(new PluginSessionRecord
		{
			SessionId = sessionId,
			PluginId = pluginId,
			DisplayName = pluginId,
			Origin = PluginSessionOrigin.Managed,
			NegotiatedVersion = 3,
			Capabilities = capabilities,
			DeclaredCapabilities = [],
			State = PluginSessionState.Awaiting,
			CreatedAt = TimeProvider.System.GetUtcNow()
		});
		_sessions.TryAttach(sessionId, new RecordingConnection(), null);
	}

	private async Task<HostResult> Invoke(string pluginId, string sessionId, string payloadJson)
	{
		var connection = new RecordingConnection();
		var envelope = new ProtocolEnvelope
		{
			Type = MessageTypes.HostInvoke,
			Id = Guid.CreateVersion7().ToString(),
			DeadlineMs = 5000,
			Payload = JsonDocument.Parse(payloadJson).RootElement.Clone()
		};

		await _runner.StartAsync(connection,
			pluginId,
			sessionId,
			envelope,
			envelope.Payload!.Value.Deserialize<HostInvokePayload>(PluginProtocolJson.Options)!,
			CancellationToken.None);

		var reply = await connection.Reply.Task.WaitAsync(TimeSpan.FromSeconds(10));
		JsonElement? data = reply.Payload is { } payload && payload.TryGetProperty("data", out var value) ? value : null;
		return new HostResult(data, reply.Error);
	}

	private sealed record HostResult(JsonElement? Data, ProtocolError? Error);

	private sealed record Delivery(string Kind, string LocalId, string Operation, JsonElement Arguments);

	private sealed class RecordingConnection : IPluginConnection
	{
		public TaskCompletionSource<ProtocolEnvelope> Reply { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public string ConnectionId { get; } = Guid.NewGuid().ToString();

		public Task Send(ProtocolEnvelope envelope, CancellationToken cancellationToken = default)
		{
			Reply.TrySetResult(envelope);
			return Task.CompletedTask;
		}

		public Task Close(int closeCode, string reason, CancellationToken cancellationToken = default) => Task.CompletedTask;
	}

	private sealed class WirePlugins : IPluginCapabilityInvoker
	{
		private readonly ConcurrentDictionary<string, ConcurrentQueue<Delivery>> _received = new(StringComparer.Ordinal);
		private readonly ConcurrentDictionary<string, JsonElement> _answers = new(StringComparer.Ordinal);

		public void Answer(string pluginId, string resultJson) => _answers[pluginId] = JsonDocument.Parse(resultJson).RootElement.Clone();

		public IReadOnlyList<Delivery> Received(string pluginId)
			=> _received.TryGetValue(pluginId, out var queue) ? [.. queue] : [];

		public async Task WaitForAsync(string pluginId, int count)
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (Received(pluginId).Count < count && DateTime.UtcNow < deadline)
			{
				await Task.Delay(10);
			}

			await Task.Delay(50);
		}

		public Task<JsonElement?> InvokeAsync(string pluginId, CapabilityInvokeRequest request, CancellationToken cancellationToken)
		{
			var arguments = JsonSerializer.SerializeToElement(request.Arguments, PluginProtocolJson.Options);
			_received.GetOrAdd(pluginId, _ => new ConcurrentQueue<Delivery>())
				.Enqueue(new Delivery(request.Kind, request.LocalId, request.Operation, arguments));
			return Task.FromResult<JsonElement?>(_answers.TryGetValue(pluginId, out var answer) ? answer : null);
		}

		public bool TryComplete(string pluginId, ProtocolEnvelope result) => false;

		public void AbortAll(string pluginId, ProtocolError reason)
		{
		}

		public bool IsLiveActionExecute(string pluginId, string correlationId) => false;
	}
}
