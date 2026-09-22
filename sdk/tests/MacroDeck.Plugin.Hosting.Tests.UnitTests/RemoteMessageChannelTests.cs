using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.Messaging;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.Messaging;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Messaging;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class RemoteMessageChannelTests
{
	private ScriptedHost _host = null!;
	private PluginConnectionState _state = null!;
	private FakePluginSocket _socket = null!;
	private RemoteMessageChannel _channel = null!;

	[SetUp]
	public void SetUp()
	{
		_host = new ScriptedHost();
		_state = new PluginConnectionState();
		_socket = new FakePluginSocket();
		_channel = new RemoteMessageChannel(_host, _state, TestMetadata.Default, TimeProvider.System, Serilog.Core.Logger.None);
	}

	[TearDown]
	public async Task TearDown()
	{
		_channel.Dispose();
		await _socket.DisposeAsync();
	}

	[Test]
	public async Task Registering_a_handler_hands_the_host_the_whole_table()
	{
		Connect();

		await _channel.SubscribeAsync("obs.*", (_, _) => Task.CompletedTask);
		await _channel.HandleRequestsAsync("obs.scene.current", (_, _) => Task.FromResult<JsonElement?>(null));

		var table = _host.Tables[^1];
		Assert.Multiple(() =>
		{
			Assert.That(table.Events, Is.EqualTo(new[] { "obs.*" }));
			Assert.That(table.Requests, Is.EqualTo(new[] { "obs.scene.current" }));
			Assert.That(table.Commands, Is.Empty);
		});
	}

	[Test]
	public void A_topic_another_participant_handles_is_refused_at_the_call_site_naming_it()
	{
		Connect();
		_host.Reject("request", "obs.scene.current", "com.example.obs");

		var exception = Assert.ThrowsAsync<MessageChannelException>(()
			=> _channel.HandleRequestsAsync("obs.scene.current", (_, _) => Task.FromResult<JsonElement?>(null)));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.ErrorCode, Is.EqualTo(MessageChannelErrorCode.TopicAlreadyHandled));
			Assert.That(exception.HandlerOwner, Is.EqualTo("com.example.obs"));
		});
	}

	[Test]
	public void An_invalid_topic_is_refused_without_asking_the_host()
	{
		Connect();

		var exception = Assert.ThrowsAsync<MessageChannelException>(()
			=> _channel.HandleCommandsAsync("Not A Topic", (_, _) => Task.CompletedTask));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.ErrorCode, Is.EqualTo(MessageChannelErrorCode.InvalidTopic));
			Assert.That(_host.Tables, Is.Empty);
		});
	}

	[Test]
	public async Task Reinitializing_keeps_the_claim_at_the_host_and_delivers_events_once()
	{
		Connect();
		var received = 0;

		await _channel.Lifecycle.SubscribeAsync("obs.scene.changed", (_, _) => Task.FromResult(Interlocked.Increment(ref received)));
		await _channel.Lifecycle.HandleRequestsAsync("obs.scene.current", (_, _) => Task.FromResult<JsonElement?>(null));
		var claimedFrom = _host.Tables.Count - 1;

		_channel.ReleaseLifecycleRegistrations();
		await _channel.Lifecycle.SubscribeAsync("obs.scene.changed", (_, _) => Task.FromResult(Interlocked.Increment(ref received)));
		await _channel.Lifecycle.HandleRequestsAsync("obs.scene.current", (_, _) => Task.FromResult<JsonElement?>(null));
		await _channel.CompleteLifecycleReleaseAsync();

		await _channel.DeliverAsync(CapabilityOperations.Messaging.Event, Delivery("obs.scene.changed"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(_host.Tables.Skip(claimedFrom).All(table => table.Requests.Contains("obs.scene.current")), Is.True,
				"every table the host saw during the pass still claims the topic");
			Assert.That(_host.Tables[^1].Events, Is.EqualTo(new[] { "obs.scene.changed" }));
			Assert.That(received, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task A_topic_not_registered_again_is_released_after_the_pass()
	{
		Connect();
		await _channel.Lifecycle.HandleCommandsAsync("lights.toggle", (_, _) => Task.CompletedTask);

		_channel.ReleaseLifecycleRegistrations();
		await _channel.CompleteLifecycleReleaseAsync();

		Assert.That(_host.Tables[^1].Commands, Is.Empty);
	}

	[Test]
	public async Task Disposing_during_shutdown_does_not_hand_the_topic_away()
	{
		Connect();
		var registration = await _channel.Lifecycle.HandleCommandsAsync("lights.toggle", (_, _) => Task.CompletedTask);
		var syncsBefore = _host.Tables.Count;

		_channel.ReleaseLifecycleRegistrations();
		await registration.DisposeAsync();

		Assert.That(_host.Tables, Has.Count.EqualTo(syncsBefore));
	}

	[Test]
	public async Task An_integration_that_registered_and_then_failed_can_register_again()
	{
		Connect();
		await _channel.Lifecycle.HandleCommandsAsync("lights.toggle", (_, _) => Task.CompletedTask);

		_channel.ReleaseLifecycleRegistrations();

		Assert.DoesNotThrowAsync(() => _channel.Lifecycle.HandleCommandsAsync("lights.toggle", (_, _) => Task.CompletedTask));
	}

	[Test]
	public async Task Registrations_made_before_the_first_connection_are_synced_on_connect()
	{
		await _channel.HandleCommandsAsync("lights.toggle", (_, _) => Task.CompletedTask);
		Assert.That(_host.Tables, Is.Empty);

		Connect();
		_state.RaiseConnected(resumed: false);

		await _host.WaitForTableAsync(table => table.Commands.Contains("lights.toggle"));
	}

	[Test]
	public void Publishing_without_a_connection_throws_not_connected()
	{
		var exception = Assert.ThrowsAsync<MessageChannelException>(() => _channel.PublishAsync("obs.scene.changed"));

		Assert.That(exception!.ErrorCode, Is.EqualTo(MessageChannelErrorCode.NotConnected));
	}

	[Test]
	public void On_a_host_without_a_message_channel_every_call_throws_unsupported()
	{
		Connect(messagingAccepted: false);

		Assert.Multiple(() =>
		{
			Assert.That(Code(() => _channel.PublishAsync("obs.scene.changed")), Is.EqualTo(MessageChannelErrorCode.Unsupported));
			Assert.That(Code(() => _channel.SubscribeAsync("obs.*", (_, _) => Task.CompletedTask)),
				Is.EqualTo(MessageChannelErrorCode.Unsupported));
		});
	}

	[Test]
	public async Task A_rate_limited_sync_is_retried()
	{
		Connect();
		_host.FailNextSubscriptions(ProtocolErrorCodes.RateLimited);

		await _channel.HandleCommandsAsync("lights.toggle", (_, _) => Task.CompletedTask);

		Assert.That(_host.Tables[^1].Commands, Is.EqualTo(new[] { "lights.toggle" }));
	}

	[Test]
	public async Task A_request_sends_its_timeout_as_the_deadline_and_waits_longer()
	{
		Connect();
		_host.Reply = new MessagingReplyPayload { Payload = JsonSerializer.SerializeToElement(new { scene = "Live" }) };

		var reply = await _channel.RequestAsync("obs.scene.current", timeout: TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(reply?.GetProperty("scene").GetString(), Is.EqualTo("Live"));
			Assert.That(_host.LastDeadline, Is.EqualTo(TimeSpan.FromSeconds(10)));
			Assert.That(_host.LastWait, Is.GreaterThan(TimeSpan.FromSeconds(10)));
		});
	}

	[Test]
	public void A_timeout_above_thirty_seconds_is_refused()
	{
		Connect();

		Assert.ThrowsAsync<ArgumentOutOfRangeException>(()
			=> _channel.RequestAsync("obs.scene.current", timeout: TimeSpan.FromSeconds(31)));
	}

	[TestCase(ProtocolErrorReasons.MessagingNoHandler, MessageChannelErrorCode.NoHandler)]
	[TestCase(ProtocolErrorReasons.MessagingHandlerFailed, MessageChannelErrorCode.HandlerFailed)]
	[TestCase(ProtocolErrorReasons.MessagingHandlerUnavailable, MessageChannelErrorCode.HandlerUnavailable)]
	public void A_failed_send_reports_why(string reason, MessageChannelErrorCode expected)
	{
		Connect();
		_host.FailNextMessage(ProtocolErrorCodes.CapabilityUnavailable, reason);

		Assert.That(Code(() => _channel.SendAsync("lights.toggle")), Is.EqualTo(expected));
	}

	[Test]
	public async Task A_delivered_request_reaches_the_handler_and_its_reply_goes_back()
	{
		Connect();
		ChannelMessage? seen = null;
		await _channel.HandleRequestsAsync("obs.scene.current",
			(message, _) =>
			{
				seen = message;
				return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement("Live"));
			});

		var result = await _channel.DeliverAsync(CapabilityOperations.Messaging.Request,
			Delivery("obs.scene.current", sender: "com.example.streamdeck"),
			CancellationToken.None);

		var reply = result.Data?.Deserialize<MessagingDeliveryResult>(PluginProtocolJson.Options);
		Assert.Multiple(() =>
		{
			Assert.That(reply?.Payload?.GetString(), Is.EqualTo("Live"));
			Assert.That(seen?.Sender, Is.EqualTo("com.example.streamdeck"));
			Assert.That(seen?.Kind, Is.EqualTo(ChannelMessageKind.Request));
		});
	}

	[Test]
	public async Task A_throwing_subscription_does_not_keep_the_others_from_the_event()
	{
		Connect();
		var received = new ConcurrentBag<string>();
		await _channel.SubscribeAsync("obs.*", (_, _) => throw new InvalidOperationException("boom"));
		await _channel.SubscribeAsync("obs.scene.changed", (message, _) =>
		{
			received.Add(message.Topic);
			return Task.CompletedTask;
		});
		await _channel.SubscribeAsync("lights.*", (message, _) =>
		{
			received.Add("wrong:" + message.Topic);
			return Task.CompletedTask;
		});

		var result = await _channel.DeliverAsync(CapabilityOperations.Messaging.Event, Delivery("obs.scene.changed"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.IsFailure, Is.False);
			Assert.That(received, Is.EqualTo(new[] { "obs.scene.changed" }));
		});
	}

	[Test]
	public async Task A_throwing_command_handler_answers_handler_failed()
	{
		Connect();
		await _channel.HandleCommandsAsync("lights.toggle", (_, _) => throw new InvalidOperationException("secret detail"));

		var result = await _channel.DeliverAsync(CapabilityOperations.Messaging.Command, Delivery("lights.toggle"), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(result.Error?.Details?["reason"], Is.EqualTo(ProtocolErrorReasons.MessagingHandlerFailed));
			Assert.That(result.Error?.Message, Does.Not.Contain("secret detail"));
		});
	}

	[Test]
	public async Task A_command_for_a_topic_this_plugin_does_not_handle_answers_no_handler()
	{
		Connect();

		var result = await _channel.DeliverAsync(CapabilityOperations.Messaging.Command, Delivery("lights.toggle"), CancellationToken.None);

		Assert.That(result.Error?.Details?["reason"], Is.EqualTo(ProtocolErrorReasons.MessagingNoHandler));
	}

	[Test]
	public void The_capability_is_declared_unless_the_host_said_it_has_no_message_channel()
	{
		var handler = new MessagingCapabilityHandler(_channel, _state);

		var unknown = handler.DeclareCapabilities();
		_state.HostMessaging = HostMessagingSupport.Advertised;
		var advertised = handler.DeclareCapabilities();
		_state.HostMessaging = HostMessagingSupport.NotAdvertised;
		var notAdvertised = handler.DeclareCapabilities();

		Assert.Multiple(() =>
		{
			Assert.That(unknown.Select(capability => (capability.Kind, capability.LocalId)),
				Is.EqualTo(new[] { (CapabilityKinds.Messaging, ProviderCapabilityId.LocalId) }));
			Assert.That(advertised, Has.Count.EqualTo(1));
			Assert.That(notAdvertised, Is.Empty);
		});
	}

	private void Connect(bool messagingAccepted = true)
	{
		_state.ActiveConnection = new PluginSessionConnection(_socket,
			TestSession.Create(),
			TestSession.Dispatcher(),
			_state,
			TimeProvider.System,
			Serilog.Core.Logger.None,
			hostInvoker: null,
			hostStateCache: null);
		_state.MessagingAccepted = messagingAccepted;
	}

	private static MessageChannelErrorCode? Code(Func<Task> call)
		=> Assert.ThrowsAsync<MessageChannelException>(() => call())?.ErrorCode;

	private static MessagingDeliveryArguments Delivery(string topic, string sender = "com.example.other")
		=> new()
		{
			Topic = topic,
			Sender = sender,
			MessageId = Guid.NewGuid().ToString(),
			SentAt = DateTimeOffset.UtcNow
		};

	private sealed class ScriptedHost : IHostInvoker
	{
		private readonly ConcurrentQueue<MessagingSubscriptionsArguments> _tables = new();
		private readonly List<MessagingRejectedTopic> _rejections = [];
		private string? _failSubscriptions;
		private (string Code, string Reason)? _failMessage;

		public IReadOnlyList<MessagingSubscriptionsArguments> Tables => [.. _tables];

		public MessagingReplyPayload? Reply { get; set; }

		public TimeSpan? LastDeadline { get; private set; }

		public TimeSpan? LastWait { get; private set; }

		public void Reject(string kind, string topic, string owner)
			=> _rejections.Add(new MessagingRejectedTopic
				{ Kind = kind, Topic = topic, Reason = ProtocolErrorReasons.MessagingTopicHandled, Owner = owner });

		public void FailNextSubscriptions(string code) => _failSubscriptions = code;

		public void FailNextMessage(string code, string reason) => _failMessage = (code, reason);

		public async Task WaitForTableAsync(Func<MessagingSubscriptionsArguments, bool> predicate)
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (!Tables.Any(predicate))
			{
				if (DateTime.UtcNow > deadline)
				{
					Assert.Fail("The expected table never reached the host.");
				}

				await Task.Delay(20);
			}
		}

		public Task<JsonElement?> InvokeAsync(string api, string operation, object? arguments, CancellationToken cancellationToken)
			=> InvokeAsync(api, operation, arguments, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(30), cancellationToken);

		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			TimeSpan timeout,
			CancellationToken cancellationToken)
			=> InvokeAsync(api, operation, arguments, timeout, timeout, cancellationToken);

		public Task<JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			TimeSpan deadline,
			TimeSpan wait,
			CancellationToken cancellationToken)
		{
			Assert.That(api, Is.EqualTo(HostApis.Messaging));

			if (operation == HostOperations.Messaging.Subscriptions)
			{
				if (_failSubscriptions is { } code)
				{
					_failSubscriptions = null;
					throw HostInvocationException.From(new ProtocolError { Code = code, Message = code, Retryable = true });
				}

				_tables.Enqueue((MessagingSubscriptionsArguments)arguments!);
				return Task.FromResult<JsonElement?>(JsonSerializer.SerializeToElement(
					new MessagingSubscriptionsResult { Rejected = [.. _rejections] },
					PluginProtocolJson.Options));
			}

			LastDeadline = deadline;
			LastWait = wait;

			if (_failMessage is { } failure)
			{
				_failMessage = null;
				throw HostInvocationException.From(new ProtocolError
				{
					Code = failure.Code,
					Message = failure.Code,
					Retryable = true,
					Details = new Dictionary<string, string> { ["reason"] = failure.Reason }
				});
			}

			return Task.FromResult(Reply is null
				? (JsonElement?)null
				: JsonSerializer.SerializeToElement(Reply, PluginProtocolJson.Options));
		}

		public bool TryComplete(ProtocolEnvelope result) => false;
	}
}
