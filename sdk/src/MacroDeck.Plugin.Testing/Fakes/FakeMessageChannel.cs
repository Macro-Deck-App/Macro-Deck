using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeck.Sdk.Messaging;

namespace MacroDeck.Plugin.Testing.Fakes;

/// <summary>
/// In-memory <see cref="IMessageChannel" /> that behaves like Macro Deck's broker for the plugin's own
/// registrations and records everything the plugin sends.
///
/// <para>
/// A published event reaches the plugin's own matching subscriptions, and a command or request reaches the
/// plugin's own handler of the topic, as it would on a real host. Anything else is answered by a stub set
/// with <see cref="RespondTo" />, or fails with <see cref="MessageChannelErrorCode.NoHandler" />. Messages
/// from other participants are simulated with <see cref="DeliverEventAsync" />,
/// <see cref="DeliverCommandAsync" /> and <see cref="DeliverRequestAsync" />.
/// </para>
/// </summary>
public sealed class FakeMessageChannel : IMessageChannel
{
	private readonly Lock _lock = new();
	private readonly List<(string Pattern, Func<ChannelMessage, CancellationToken, Task> Handler)> _subscriptions = [];
	private readonly Dictionary<string, Func<ChannelMessage, CancellationToken, Task>> _commands = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Func<ChannelMessage, CancellationToken, Task<JsonElement?>>> _requests
		= new(StringComparer.Ordinal);
	private readonly ConcurrentDictionary<string, Func<ChannelMessage, JsonElement?>> _stubs = new(StringComparer.Ordinal);
	private readonly ConcurrentQueue<ChannelMessage> _published = new();
	private readonly ConcurrentQueue<ChannelMessage> _sent = new();
	private readonly ConcurrentQueue<ChannelMessage> _requested = new();

	/// <summary>The sender id stamped on what the plugin under test sends. Defaults to <c>plugin-under-test</c>.</summary>
	public string SenderId { get; set; } = "plugin-under-test";

	/// <summary>Every event the plugin published, in order.</summary>
	public IReadOnlyList<ChannelMessage> Published => [.. _published];

	/// <summary>Every command the plugin sent, in order.</summary>
	public IReadOnlyList<ChannelMessage> Sent => [.. _sent];

	/// <summary>Every request the plugin sent, in order.</summary>
	public IReadOnlyList<ChannelMessage> Requested => [.. _requested];

	/// <summary>The patterns the plugin currently subscribes to.</summary>
	public IReadOnlyList<string> Subscriptions
	{
		get
		{
			lock (_lock)
			{
				return [.. _subscriptions.Select(subscription => subscription.Pattern)];
			}
		}
	}

	/// <summary>The command topics the plugin currently handles.</summary>
	public IReadOnlyList<string> HandledCommands
	{
		get
		{
			lock (_lock)
			{
				return [.. _commands.Keys];
			}
		}
	}

	/// <summary>The request topics the plugin currently handles.</summary>
	public IReadOnlyList<string> HandledRequests
	{
		get
		{
			lock (_lock)
			{
				return [.. _requests.Keys];
			}
		}
	}

	/// <summary>
	/// Answers commands and requests the plugin sends to <paramref name="topic" /> when the plugin does
	/// not handle it itself. For a command the returned value is ignored.
	/// </summary>
	public void RespondTo(string topic, Func<ChannelMessage, JsonElement?> reply)
	{
		ArgumentNullException.ThrowIfNull(reply);
		_stubs[topic] = reply;
	}

	public async Task PublishAsync(string topic, JsonElement? payload = null, CancellationToken cancellationToken = default)
	{
		var message = Message(ChannelMessageKind.Event, topic, payload, SenderId);
		_published.Enqueue(message);
		await DeliverToSubscriptionsAsync(message, cancellationToken).ConfigureAwait(false);
	}

	public async Task SendAsync(string topic,
		JsonElement? payload = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		var message = Message(ChannelMessageKind.Command, topic, payload, SenderId);
		_sent.Enqueue(message);

		Func<ChannelMessage, CancellationToken, Task>? handler;
		lock (_lock)
		{
			_commands.TryGetValue(topic, out handler);
		}

		if (handler is not null)
		{
			await RunAsync(topic, () => handler(message, cancellationToken)).ConfigureAwait(false);
			return;
		}

		_ = Stub(topic)(message);
	}

	public async Task<JsonElement?> RequestAsync(string topic,
		JsonElement? payload = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		var message = Message(ChannelMessageKind.Request, topic, payload, SenderId);
		_requested.Enqueue(message);

		Func<ChannelMessage, CancellationToken, Task<JsonElement?>>? handler;
		lock (_lock)
		{
			_requests.TryGetValue(topic, out handler);
		}

		return handler is not null
			? await RunAsync(topic, () => handler(message, cancellationToken)).ConfigureAwait(false)
			: Stub(topic)(message);
	}

	public Task<IAsyncDisposable> SubscribeAsync(string topicPattern,
		Func<ChannelMessage, CancellationToken, Task> handler,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(handler);
		ThrowIfInvalid(topicPattern, MessageTopic.IsValidPattern(topicPattern));

		var entry = (topicPattern, handler);
		lock (_lock)
		{
			_subscriptions.Add(entry);
		}

		return Task.FromResult<IAsyncDisposable>(new Registration(() =>
		{
			lock (_lock)
			{
				_subscriptions.Remove(entry);
			}
		}));
	}

	public Task<IAsyncDisposable> HandleCommandsAsync(string topic,
		Func<ChannelMessage, CancellationToken, Task> handler,
		CancellationToken cancellationToken = default)
		=> Claim(_commands, topic, handler);

	public Task<IAsyncDisposable> HandleRequestsAsync(string topic,
		Func<ChannelMessage, CancellationToken, Task<JsonElement?>> handler,
		CancellationToken cancellationToken = default)
		=> Claim(_requests, topic, handler);

	/// <summary>Delivers an event from another participant to the plugin's matching subscriptions.</summary>
	public Task DeliverEventAsync(string topic,
		JsonElement? payload = null,
		string sender = "test-sender",
		CancellationToken cancellationToken = default)
		=> DeliverToSubscriptionsAsync(Message(ChannelMessageKind.Event, topic, payload, sender), cancellationToken);

	/// <summary>Delivers a command from another participant to the plugin's handler of <paramref name="topic" />.</summary>
	/// <exception cref="MessageChannelException">The plugin does not handle the topic, or its handler threw.</exception>
	public async Task DeliverCommandAsync(string topic,
		JsonElement? payload = null,
		string sender = "test-sender",
		CancellationToken cancellationToken = default)
	{
		Func<ChannelMessage, CancellationToken, Task>? handler;
		lock (_lock)
		{
			_commands.TryGetValue(topic, out handler);
		}

		if (handler is null)
		{
			throw NoHandler(topic);
		}

		await RunAsync(topic, () => handler(Message(ChannelMessageKind.Command, topic, payload, sender), cancellationToken))
			.ConfigureAwait(false);
	}

	/// <summary>Delivers a request from another participant to the plugin's handler and returns its reply.</summary>
	/// <exception cref="MessageChannelException">The plugin does not handle the topic, or its handler threw.</exception>
	public async Task<JsonElement?> DeliverRequestAsync(string topic,
		JsonElement? payload = null,
		string sender = "test-sender",
		CancellationToken cancellationToken = default)
	{
		Func<ChannelMessage, CancellationToken, Task<JsonElement?>>? handler;
		lock (_lock)
		{
			_requests.TryGetValue(topic, out handler);
		}

		if (handler is null)
		{
			throw NoHandler(topic);
		}

		return await RunAsync(topic,
				() => handler(Message(ChannelMessageKind.Request, topic, payload, sender), cancellationToken))
			.ConfigureAwait(false);
	}

	private Task<IAsyncDisposable> Claim<THandler>(Dictionary<string, THandler> handlers, string topic, THandler handler)
		where THandler : Delegate
	{
		ArgumentNullException.ThrowIfNull(handler);
		ThrowIfInvalid(topic, MessageTopic.IsValidTopic(topic));

		lock (_lock)
		{
			if (!handlers.TryAdd(topic, handler))
			{
				throw new MessageChannelException(MessageChannelErrorCode.TopicAlreadyHandled,
					topic,
					$"'{topic}' is already handled.",
					SenderId);
			}
		}

		return Task.FromResult<IAsyncDisposable>(new Registration(() =>
		{
			lock (_lock)
			{
				handlers.Remove(topic);
			}
		}));
	}

	private async Task DeliverToSubscriptionsAsync(ChannelMessage message, CancellationToken cancellationToken)
	{
		Func<ChannelMessage, CancellationToken, Task>[] matching;
		lock (_lock)
		{
			matching =
			[
				.. _subscriptions.Where(subscription => MessageTopic.Matches(subscription.Pattern, message.Topic))
					.Select(subscription => subscription.Handler)
			];
		}

		foreach (var handler in matching)
		{
			await handler(message, cancellationToken).ConfigureAwait(false);
		}
	}

	private Func<ChannelMessage, JsonElement?> Stub(string topic)
		=> _stubs.TryGetValue(topic, out var stub) ? stub : _ => throw NoHandler(topic);

	private static async Task RunAsync(string topic, Func<Task> run)
		=> await RunAsync(topic,
				async () =>
				{
					await run().ConfigureAwait(false);
					return null;
				})
			.ConfigureAwait(false);

	private static async Task<JsonElement?> RunAsync(string topic, Func<Task<JsonElement?>> run)
	{
		try
		{
			return await run().ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not (OperationCanceledException or MessageChannelException))
		{
			throw new MessageChannelException(MessageChannelErrorCode.HandlerFailed,
				topic,
				$"The handler of '{topic}' failed: {exception.Message}");
		}
	}

	private static void ThrowIfInvalid(string topic, bool valid)
	{
		if (!valid)
		{
			throw new MessageChannelException(MessageChannelErrorCode.InvalidTopic, topic, $"'{topic}' is not a valid topic.");
		}
	}

	private static MessageChannelException NoHandler(string topic)
		=> new(MessageChannelErrorCode.NoHandler, topic, $"Nothing handles '{topic}'.");

	private static ChannelMessage Message(ChannelMessageKind kind, string topic, JsonElement? payload, string sender)
	{
		if (!MessageTopic.IsValidTopic(topic))
		{
			throw new MessageChannelException(MessageChannelErrorCode.InvalidTopic, topic, $"'{topic}' is not a valid topic.");
		}

		return new ChannelMessage
		{
			Kind = kind,
			Topic = topic,
			Sender = sender,
			MessageId = Guid.NewGuid().ToString(),
			SentAt = DateTimeOffset.UtcNow,
			Payload = payload?.Clone()
		};
	}

	private sealed class Registration(Action remove) : IAsyncDisposable
	{
		private int _disposed;

		public ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				remove();
			}

			return ValueTask.CompletedTask;
		}
	}
}
