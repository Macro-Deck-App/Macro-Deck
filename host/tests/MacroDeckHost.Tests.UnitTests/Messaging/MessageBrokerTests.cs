using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Sdk.Messaging;
using MacroDeckHost.Application.Messaging;

namespace MacroDeckHost.Tests.UnitTests.Messaging;

[TestFixture]
public class MessageBrokerTests
{
	private MessageBroker _broker = null!;

	[SetUp]
	public void SetUp() => _broker = new MessageBroker(Serilog.Core.Logger.None);

	[TearDown]
	public void TearDown() => _broker.Dispose();

	[Test]
	public async Task An_event_reaches_every_matching_subscription_including_the_senders_own()
	{
		var obs = Participant("com.example.obs", events: ["obs.scene.changed"]);
		var deck = Participant("com.example.deck", events: ["obs.*"]);
		var lights = Participant("com.example.lights", events: ["lights.*"]);

		_broker.Publish("com.example.obs", "obs.scene.changed", Json(new { scene = "Live" }));

		await Task.WhenAll(obs.WaitForEventsAsync(1), deck.WaitForEventsAsync(1));
		await Task.Delay(50);

		Assert.Multiple(() =>
		{
			Assert.That(deck.Events.Single().Sender, Is.EqualTo("com.example.obs"));
			Assert.That(deck.Events.Single().Payload?.GetProperty("scene").GetString(), Is.EqualTo("Live"));
			Assert.That(obs.Events, Has.Count.EqualTo(1));
			Assert.That(lights.Events, Is.Empty);
		});
	}

	[Test]
	public async Task A_subscriber_receives_events_in_publish_order()
	{
		var deck = Participant("com.example.deck", events: ["obs.*"]);

		for (var index = 0; index < 20; index++)
		{
			_broker.Publish("com.example.obs", "obs.scene.changed", Json(index));
		}

		await deck.WaitForEventsAsync(20);

		Assert.That(deck.Events.Select(message => message.Payload!.Value.GetInt32()), Is.EqualTo(Enumerable.Range(0, 20)));
	}

	[Test]
	public async Task A_slow_subscriber_does_not_hold_up_the_others()
	{
		var slow = Participant("com.example.slow", events: ["obs.*"]);
		slow.EventGate = new TaskCompletionSource();
		var fast = Participant("com.example.fast", events: ["obs.*"]);

		_broker.Publish("com.example.obs", "obs.scene.changed", null);
		_broker.Publish("com.example.obs", "obs.scene.changed", null);

		await fast.WaitForEventsAsync(2);
		slow.EventGate.SetResult();
		await slow.WaitForEventsAsync(2);
	}

	[Test]
	public async Task A_request_reaches_the_topics_one_handler_and_returns_its_reply()
	{
		await using var obs = new InProcessMessageChannel(_broker, "obs", Serilog.Core.Logger.None);
		await using var deck = new InProcessMessageChannel(_broker, "deck", Serilog.Core.Logger.None);
		await obs.HandleRequestsAsync("obs.scene.current",
			(message, _) => Task.FromResult<JsonElement?>(Json($"asked by {message.Sender}")));

		var reply = await deck.RequestAsync("obs.scene.current");

		Assert.That(reply?.GetString(), Is.EqualTo("asked by deck"));
	}

	[Test]
	public async Task A_second_participant_cannot_take_a_handled_topic_and_learns_who_has_it()
	{
		await using var obs = new InProcessMessageChannel(_broker, "obs", Serilog.Core.Logger.None);
		await using var intruder = new InProcessMessageChannel(_broker, "intruder", Serilog.Core.Logger.None);
		await obs.HandleCommandsAsync("obs.scene.switch", (_, _) => Task.CompletedTask);

		var exception = Assert.ThrowsAsync<MessageChannelException>(()
			=> intruder.HandleCommandsAsync("obs.scene.switch", (_, _) => Task.CompletedTask));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.ErrorCode, Is.EqualTo(MessageChannelErrorCode.TopicAlreadyHandled));
			Assert.That(exception.HandlerOwner, Is.EqualTo("obs"));
		});
	}

	[Test]
	public void A_request_nobody_handles_fails_with_no_handler()
	{
		var exception = Assert.ThrowsAsync<MessageChannelException>(()
			=> _broker.RequestAsync("deck", "obs.scene.current", null, TimeSpan.FromSeconds(1), CancellationToken.None));

		Assert.That(exception!.ErrorCode, Is.EqualTo(MessageChannelErrorCode.NoHandler));
	}

	[Test]
	public void A_participant_that_goes_away_releases_its_topics()
	{
		Participant("com.example.obs", "session-1", requests: ["obs.scene.current"]);

		_broker.Remove("com.example.obs", "session-1");
		var rejected = _broker.Replace("com.example.other",
			"session-9",
			new RecordingParticipant(),
			new MessageRegistrations([], [], ["obs.scene.current"]));

		Assert.That(rejected, Is.Empty);
	}

	[Test]
	public void A_replaced_session_cannot_remove_the_registrations_of_its_successor()
	{
		Participant("com.example.obs", "session-1", requests: ["obs.scene.current"]);
		Participant("com.example.obs", "session-2", requests: ["obs.scene.current"]);

		_broker.Remove("com.example.obs", "session-1");

		Assert.That(_broker.CurrentRegistrationKey("com.example.obs"), Is.EqualTo("session-2"));
	}

	[Test]
	public void An_invalid_topic_is_rejected_with_its_reason()
	{
		var rejected = _broker.Replace("com.example.obs",
			"session-1",
			new RecordingParticipant(),
			new MessageRegistrations(["Obs.*"], ["single"], []));

		Assert.That(rejected.Select(rejection => (rejection.Topic, rejection.Reason)),
			Is.EquivalentTo(new[]
			{
				("Obs.*", ProtocolErrorReasons.MessagingInvalidTopic), ("single", ProtocolErrorReasons.MessagingInvalidTopic)
			}));
	}

	[Test]
	public void A_handler_that_does_not_answer_in_time_times_out()
	{
		var obs = Participant("com.example.obs", requests: ["obs.scene.current"]);
		obs.RequestGate = new TaskCompletionSource();

		var exception = Assert.ThrowsAsync<MessageChannelException>(()
			=> _broker.RequestAsync("deck", "obs.scene.current", null, TimeSpan.FromMilliseconds(100), CancellationToken.None));

		Assert.That(exception!.ErrorCode, Is.EqualTo(MessageChannelErrorCode.Timeout));
	}

	[Test]
	public async Task A_busy_handler_refuses_more_requests_instead_of_queueing_them()
	{
		var obs = Participant("com.example.obs", requests: ["obs.scene.current"]);
		obs.RequestGate = new TaskCompletionSource();

		var pending = Enumerable.Range(0, MessageBroker.MaxHandlerDeliveriesInFlight)
			.Select(_ => _broker.RequestAsync("deck", "obs.scene.current", null, TimeSpan.FromSeconds(5), CancellationToken.None))
			.ToList();
		await obs.WaitForRequestsAsync(MessageBroker.MaxHandlerDeliveriesInFlight);

		var exception = Assert.ThrowsAsync<MessageChannelException>(()
			=> _broker.RequestAsync("deck", "obs.scene.current", null, TimeSpan.FromSeconds(5), CancellationToken.None));

		obs.RequestGate.SetResult();
		await Task.WhenAll(pending);

		Assert.That(exception!.ErrorCode, Is.EqualTo(MessageChannelErrorCode.RateLimited));
	}

	[Test]
	public async Task A_throwing_built_in_handler_fails_the_request_without_its_text()
	{
		await using var obs = new InProcessMessageChannel(_broker, "obs", Serilog.Core.Logger.None);
		await using var deck = new InProcessMessageChannel(_broker, "deck", Serilog.Core.Logger.None);
		await obs.HandleRequestsAsync("obs.scene.current", (_, _) => throw new InvalidOperationException("C:\\secret"));

		var exception = Assert.ThrowsAsync<MessageChannelException>(() => deck.RequestAsync("obs.scene.current"));

		Assert.Multiple(() =>
		{
			Assert.That(exception!.ErrorCode, Is.EqualTo(MessageChannelErrorCode.HandlerFailed));
			Assert.That(exception.Message, Does.Not.Contain("secret"));
		});
	}

	[Test]
	public async Task A_built_in_that_shuts_down_releases_its_topics()
	{
		var obs = new InProcessMessageChannel(_broker, "obs", Serilog.Core.Logger.None);
		await obs.HandleCommandsAsync("obs.scene.switch", (_, _) => Task.CompletedTask);

		await obs.DisposeAsync();

		await using var successor = new InProcessMessageChannel(_broker, "other", Serilog.Core.Logger.None);
		Assert.DoesNotThrowAsync(() => successor.HandleCommandsAsync("obs.scene.switch", (_, _) => Task.CompletedTask));
	}

	private RecordingParticipant Participant(string id,
		string key = "session-1",
		string[]? events = null,
		string[]? requests = null)
	{
		var participant = new RecordingParticipant();
		_broker.Replace(id, key, participant, new MessageRegistrations(events ?? [], [], requests ?? []));
		return participant;
	}

	private static JsonElement Json<T>(T value) => JsonSerializer.SerializeToElement(value);

	private sealed class RecordingParticipant : IMessageParticipant
	{
		private readonly ConcurrentQueue<MessageDelivery> _events = new();
		private int _requests;

		public TaskCompletionSource? EventGate { get; set; }

		public TaskCompletionSource? RequestGate { get; set; }

		public IReadOnlyList<MessageDelivery> Events => [.. _events];

		public async Task DeliverEventAsync(MessageDelivery message, CancellationToken cancellationToken)
		{
			if (EventGate is { } gate)
			{
				await gate.Task.WaitAsync(cancellationToken);
			}

			_events.Enqueue(message);
		}

		public Task HandleCommandAsync(MessageDelivery message, TimeSpan timeout, CancellationToken cancellationToken)
			=> Task.CompletedTask;

		public async Task<JsonElement?> HandleRequestAsync(MessageDelivery message,
			TimeSpan timeout,
			CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref _requests);
			if (RequestGate is { } gate)
			{
				await gate.Task.WaitAsync(cancellationToken);
			}

			return null;
		}

		public async Task WaitForEventsAsync(int count)
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (_events.Count < count && DateTime.UtcNow < deadline)
			{
				await Task.Delay(10);
			}

			Assert.That(_events, Has.Count.GreaterThanOrEqualTo(count));
		}

		public async Task WaitForRequestsAsync(int count)
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (Volatile.Read(ref _requests) < count && DateTime.UtcNow < deadline)
			{
				await Task.Delay(10);
			}
		}
	}
}
