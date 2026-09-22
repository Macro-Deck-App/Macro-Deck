using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Messaging;
using Serilog;

namespace MacroDeckHost.Application.Messaging;

public sealed class InProcessMessageChannel : IMessageChannel, IMessageParticipant, IAsyncDisposable
{
	private readonly IMessageBroker _broker;
	private readonly string _integrationId;
	private readonly string _registrationKey = Guid.NewGuid().ToString("N");
	private readonly ILogger _logger;
	private readonly Lock _lock = new();
	private readonly List<Subscription> _subscriptions = [];
	private readonly Dictionary<string, Func<ChannelMessage, CancellationToken, Task>> _commands = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Func<ChannelMessage, CancellationToken, Task<JsonElement?>>> _requests
		= new(StringComparer.Ordinal);

	private bool _disposed;

	public InProcessMessageChannel(IMessageBroker broker, string integrationId, ILogger logger)
	{
		ArgumentException.ThrowIfNullOrEmpty(integrationId);
		_broker = broker;
		_integrationId = integrationId;
		_logger = logger.ForContext<InProcessMessageChannel>();
	}

	public Task PublishAsync(string topic, JsonElement? payload = null, CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed(topic);
		_broker.Publish(_integrationId, topic, payload);
		return Task.CompletedTask;
	}

	public Task SendAsync(string topic,
		JsonElement? payload = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed(topic);
		return _broker.SendAsync(_integrationId, topic, payload, ValidTimeout(timeout), cancellationToken);
	}

	public Task<JsonElement?> RequestAsync(string topic,
		JsonElement? payload = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		ThrowIfDisposed(topic);
		return _broker.RequestAsync(_integrationId, topic, payload, ValidTimeout(timeout), cancellationToken);
	}

	public Task<IAsyncDisposable> SubscribeAsync(string topicPattern,
		Func<ChannelMessage, CancellationToken, Task> handler,
		CancellationToken cancellationToken = default)
	{
		ArgumentNullException.ThrowIfNull(handler);
		ThrowIfDisposed(topicPattern);

		var subscription = new Subscription(topicPattern, handler);
		lock (_lock)
		{
			ThrowIfDisposed(topicPattern);
			_subscriptions.Add(subscription);
			Sync(ChannelMessageKind.Event, topicPattern, () => _subscriptions.Remove(subscription));
		}

		return Task.FromResult<IAsyncDisposable>(new Registration(this, () => _subscriptions.Remove(subscription)));
	}

	public Task<IAsyncDisposable> HandleCommandsAsync(string topic,
		Func<ChannelMessage, CancellationToken, Task> handler,
		CancellationToken cancellationToken = default)
		=> Claim(_commands, ChannelMessageKind.Command, topic, handler);

	public Task<IAsyncDisposable> HandleRequestsAsync(string topic,
		Func<ChannelMessage, CancellationToken, Task<JsonElement?>> handler,
		CancellationToken cancellationToken = default)
		=> Claim(_requests, ChannelMessageKind.Request, topic, handler);

	public async Task DeliverEventAsync(MessageDelivery message, CancellationToken cancellationToken)
	{
		Subscription[] matching;
		lock (_lock)
		{
			matching = [.. _subscriptions.Where(subscription => MessageTopic.Matches(subscription.Pattern, message.Topic))];
		}

		var received = ToChannelMessage(ChannelMessageKind.Event, message);
		foreach (var subscription in matching)
		{
			try
			{
				await subscription.Handler(received, cancellationToken).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OperationCanceledException)
			{
				_logger.Warning(exception,
					"A subscription of {IntegrationId} to {Pattern} failed on {Topic}",
					_integrationId,
					subscription.Pattern,
					message.Topic);
			}
		}
	}

	public async Task HandleCommandAsync(MessageDelivery message, TimeSpan timeout, CancellationToken cancellationToken)
	{
		Func<ChannelMessage, CancellationToken, Task>? handler;
		lock (_lock)
		{
			_commands.TryGetValue(message.Topic, out handler);
		}

		await RunHandlerAsync(message.Topic,
				async () =>
				{
					if (handler is null)
					{
						throw NoHandler(message.Topic);
					}

					await handler(ToChannelMessage(ChannelMessageKind.Command, message), cancellationToken)
						.ConfigureAwait(false);
					return null;
				})
			.ConfigureAwait(false);
	}

	public Task<JsonElement?> HandleRequestAsync(MessageDelivery message,
		TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		Func<ChannelMessage, CancellationToken, Task<JsonElement?>>? handler;
		lock (_lock)
		{
			_requests.TryGetValue(message.Topic, out handler);
		}

		return RunHandlerAsync(message.Topic,
			() => handler is null
				? throw NoHandler(message.Topic)
				: handler(ToChannelMessage(ChannelMessageKind.Request, message), cancellationToken));
	}

	public ValueTask DisposeAsync()
	{
		lock (_lock)
		{
			_disposed = true;
			_subscriptions.Clear();
			_commands.Clear();
			_requests.Clear();
		}

		_broker.Remove(_integrationId, _registrationKey);
		return ValueTask.CompletedTask;
	}

	private Task<IAsyncDisposable> Claim<THandler>(Dictionary<string, THandler> handlers,
		ChannelMessageKind kind,
		string topic,
		THandler handler)
		where THandler : Delegate
	{
		ArgumentNullException.ThrowIfNull(handler);
		ThrowIfDisposed(topic);

		lock (_lock)
		{
			ThrowIfDisposed(topic);
			if (!handlers.TryAdd(topic, handler))
			{
				throw new MessageChannelException(MessageChannelErrorCode.TopicAlreadyHandled,
					topic,
					$"'{topic}' is already handled by {_integrationId}.",
					_integrationId);
			}

			Sync(kind, topic, () => handlers.Remove(topic));
		}

		return Task.FromResult<IAsyncDisposable>(new Registration(this, () => handlers.Remove(topic)));
	}

	private void Sync(ChannelMessageKind kind, string topic, Action undo)
	{
		var rejected = _broker.Replace(_integrationId,
			_registrationKey,
			this,
			CurrentRegistrations());

		var own = rejected.FirstOrDefault(rejection => rejection.Kind == kind &&
			string.Equals(rejection.Topic, topic, StringComparison.Ordinal));
		if (own is null)
		{
			return;
		}

		undo();
		_broker.Replace(_integrationId,
			_registrationKey,
			this,
			CurrentRegistrations());

		throw string.Equals(own.Reason, ProtocolErrorReasons.MessagingTopicHandled, StringComparison.Ordinal)
			? new MessageChannelException(MessageChannelErrorCode.TopicAlreadyHandled,
				topic,
				$"'{topic}' is already handled by {own.Owner}.",
				own.Owner)
			: new MessageChannelException(MessageChannelErrorCode.InvalidTopic, topic, $"'{topic}' is not a valid topic.");
	}

	private void Unregister(Action remove)
	{
		lock (_lock)
		{
			if (_disposed)
			{
				return;
			}

			remove();
			_broker.Replace(_integrationId,
				_registrationKey,
				this,
				CurrentRegistrations());
		}
	}

	private MessageRegistrations CurrentRegistrations()
		=> new([.. _subscriptions.Select(subscription => subscription.Pattern)], [.. _commands.Keys], [.. _requests.Keys]);

	private async Task<JsonElement?> RunHandlerAsync(string topic, Func<Task<JsonElement?>> run)
	{
		try
		{
			var reply = await Task.Run(run).ConfigureAwait(false);
			if (reply is { } element &&
				Encoding.UTF8.GetByteCount(element.GetRawText()) > MessagingLimits.MaxPayloadBytes)
			{
				throw new MessageChannelException(MessageChannelErrorCode.PayloadTooLarge,
					topic,
					$"The reply exceeds {MessagingLimits.MaxPayloadBytes} bytes.");
			}

			return reply;
		}
		catch (Exception exception) when (exception is not (OperationCanceledException or MessageChannelException))
		{
			_logger.Warning(exception, "The handler of {Topic} in {IntegrationId} failed", topic, _integrationId);
			throw new MessageChannelException(MessageChannelErrorCode.HandlerFailed,
				topic,
				$"The handler of '{topic}' failed.");
		}
	}

	private void ThrowIfDisposed(string? topic)
	{
		if (Volatile.Read(ref _disposed))
		{
			throw new MessageChannelException(MessageChannelErrorCode.Unsupported,
				topic,
				"This integration has been shut down; its message channel is closed.");
		}
	}

	private static TimeSpan ValidTimeout(TimeSpan? timeout)
	{
		var value = timeout ?? MessagingLimits.MaxHandlerTimeout;
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero, nameof(timeout));
		ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MessagingLimits.MaxHandlerTimeout, nameof(timeout));
		return value;
	}

	private static MessageChannelException NoHandler(string topic)
		=> new(MessageChannelErrorCode.NoHandler, topic, $"Nothing handles '{topic}'.");

	private static ChannelMessage ToChannelMessage(ChannelMessageKind kind, MessageDelivery message)
		=> new()
		{
			Kind = kind,
			Topic = message.Topic,
			Sender = message.Sender,
			MessageId = message.MessageId,
			SentAt = message.SentAt,
			Payload = message.Payload
		};

	private sealed record Subscription(string Pattern, Func<ChannelMessage, CancellationToken, Task> Handler);

	private sealed class Registration(InProcessMessageChannel channel, Action remove) : IAsyncDisposable
	{
		private int _disposed;

		public ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				channel.Unregister(remove);
			}

			return ValueTask.CompletedTask;
		}
	}
}
