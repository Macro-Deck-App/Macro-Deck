using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Messaging;
using Serilog;

namespace MacroDeckHost.Application.Messaging;

public sealed class MessageBroker : IMessageBroker, IDisposable
{
	public const int EventQueueCapacity = 256;

	public const int MaxHandlerDeliveriesInFlight = 8;

	private static readonly TimeSpan _dropLogInterval = TimeSpan.FromMinutes(1);

	private readonly Lock _lock = new();
	private readonly Dictionary<string, Participant> _participants = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Participant> _commandOwners = new(StringComparer.Ordinal);
	private readonly Dictionary<string, Participant> _requestOwners = new(StringComparer.Ordinal);
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;

	private Participant[] _snapshot = [];

	public MessageBroker(ILogger logger, TimeProvider? timeProvider = null)
	{
		_logger = logger.ForContext<MessageBroker>();
		_timeProvider = timeProvider ?? TimeProvider.System;
	}

	public IReadOnlyList<MessageRegistrationRejection> Replace(string participantId,
		string registrationKey,
		IMessageParticipant participant,
		MessageRegistrations registrations)
	{
		ArgumentException.ThrowIfNullOrEmpty(participantId);
		ArgumentException.ThrowIfNullOrEmpty(registrationKey);
		ArgumentNullException.ThrowIfNull(participant);
		ArgumentNullException.ThrowIfNull(registrations);

		var rejected = new List<MessageRegistrationRejection>();
		Participant? retired = null;

		lock (_lock)
		{
			if (_participants.TryGetValue(participantId, out var existing) &&
				!string.Equals(existing.RegistrationKey, registrationKey, StringComparison.Ordinal))
			{
				ReleaseClaims(existing);
				_participants.Remove(participantId);
				retired = existing;
				existing = null;
			}

			var state = existing ?? new Participant(participantId, registrationKey, this);
			state.Endpoint = participant;

			state.Patterns = [.. Accept(ChannelMessageKind.Event, registrations.Events, MessageTopic.IsValidPattern, rejected)];

			ReleaseClaims(state);
			state.Commands = Claim(state,
				ChannelMessageKind.Command,
				Accept(ChannelMessageKind.Command, registrations.Commands, MessageTopic.IsValidTopic, rejected),
				_commandOwners,
				rejected);
			state.Requests = Claim(state,
				ChannelMessageKind.Request,
				Accept(ChannelMessageKind.Request, registrations.Requests, MessageTopic.IsValidTopic, rejected),
				_requestOwners,
				rejected);

			_participants[participantId] = state;
			_snapshot = [.. _participants.Values];
		}

		retired?.Stop();

		foreach (var rejection in rejected.Where(rejection => rejection.Owner is not null))
		{
			_logger.Information(
				"{ParticipantId} cannot handle {Kind} topic {Topic}: {Owner} already handles it",
				participantId,
				rejection.Kind,
				rejection.Topic,
				rejection.Owner);
		}

		return rejected;
	}

	public void Remove(string participantId, string registrationKey)
	{
		Participant? removed;

		lock (_lock)
		{
			if (!_participants.TryGetValue(participantId, out removed) ||
				!string.Equals(removed.RegistrationKey, registrationKey, StringComparison.Ordinal))
			{
				return;
			}

			ReleaseClaims(removed);
			_participants.Remove(participantId);
			_snapshot = [.. _participants.Values];
		}

		removed.Stop();
	}

	public string? CurrentRegistrationKey(string participantId)
	{
		lock (_lock)
		{
			return _participants.TryGetValue(participantId, out var participant) ? participant.RegistrationKey : null;
		}
	}

	public void Publish(string senderId, string topic, JsonElement? payload)
	{
		ArgumentException.ThrowIfNullOrEmpty(senderId);
		EnsureValid(topic, payload);

		var message = NewMessage(senderId, topic, payload);
		foreach (var participant in Volatile.Read(ref _snapshot))
		{
			if (participant.Patterns.Any(pattern => MessageTopic.Matches(pattern, topic)))
			{
				participant.Enqueue(message);
			}
		}
	}

	public async Task SendAsync(string senderId,
		string topic,
		JsonElement? payload,
		TimeSpan timeout,
		CancellationToken cancellationToken)
		=> await DeliverToOwnerAsync(senderId,
				topic,
				payload,
				timeout,
				_commandOwners,
				async (endpoint, message, token) =>
				{
					await endpoint.HandleCommandAsync(message, timeout, token).ConfigureAwait(false);
					return null;
				},
				cancellationToken)
			.ConfigureAwait(false);

	public Task<JsonElement?> RequestAsync(string senderId,
		string topic,
		JsonElement? payload,
		TimeSpan timeout,
		CancellationToken cancellationToken)
		=> DeliverToOwnerAsync(senderId,
			topic,
			payload,
			timeout,
			_requestOwners,
			(endpoint, message, token) => endpoint.HandleRequestAsync(message, timeout, token),
			cancellationToken);

	public void Dispose()
	{
		Participant[] participants;
		lock (_lock)
		{
			participants = [.. _participants.Values];
			_participants.Clear();
			_commandOwners.Clear();
			_requestOwners.Clear();
			_snapshot = [];
		}

		foreach (var participant in participants)
		{
			participant.Dispose();
		}
	}

	private async Task<JsonElement?> DeliverToOwnerAsync(string senderId,
		string topic,
		JsonElement? payload,
		TimeSpan timeout,
		Dictionary<string, Participant> owners,
		Func<IMessageParticipant, MessageDelivery, CancellationToken, Task<JsonElement?>> deliver,
		CancellationToken cancellationToken)
	{
		ArgumentException.ThrowIfNullOrEmpty(senderId);
		EnsureValid(topic, payload);

		if (timeout <= TimeSpan.Zero || timeout > MessagingLimits.MaxHandlerTimeout)
		{
			timeout = MessagingLimits.MaxHandlerTimeout;
		}

		Participant? owner;
		lock (_lock)
		{
			owners.TryGetValue(topic, out owner);
		}

		if (owner is null)
		{
			throw new MessageChannelException(MessageChannelErrorCode.NoHandler, topic, $"Nothing handles '{topic}'.");
		}

		if (!owner.TryEnterHandler())
		{
			throw new MessageChannelException(MessageChannelErrorCode.RateLimited,
				topic,
				$"The handler of '{topic}' is already busy with {MaxHandlerDeliveriesInFlight} messages.");
		}

		using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, owner.Stopping);
		deadline.CancelAfter(timeout);

		try
		{
			return await deliver(owner.Endpoint, NewMessage(senderId, topic, payload), deadline.Token)
				.ConfigureAwait(false);
		}
		catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
		{
			throw owner.Stopping.IsCancellationRequested
				? new MessageChannelException(MessageChannelErrorCode.HandlerUnavailable,
					topic,
					$"The handler of '{topic}' went away.")
				: new MessageChannelException(MessageChannelErrorCode.Timeout,
					topic,
					$"The handler of '{topic}' did not answer in time.");
		}
		finally
		{
			owner.LeaveHandler();
		}
	}

	private static IEnumerable<string> Accept(ChannelMessageKind kind,
		IReadOnlyList<string> entries,
		Func<string?, bool> isValid,
		List<MessageRegistrationRejection> rejected)
	{
		foreach (var entry in entries.Distinct(StringComparer.Ordinal))
		{
			if (isValid(entry))
			{
				yield return entry;
			}
			else
			{
				rejected.Add(new MessageRegistrationRejection(kind, entry, ProtocolErrorReasons.MessagingInvalidTopic, null));
			}
		}
	}

	private static string[] Claim(Participant state,
		ChannelMessageKind kind,
		IEnumerable<string> topics,
		Dictionary<string, Participant> owners,
		List<MessageRegistrationRejection> rejected)
	{
		var claimed = new List<string>();
		foreach (var topic in topics)
		{
			if (owners.TryGetValue(topic, out var owner) && !ReferenceEquals(owner, state))
			{
				rejected.Add(new MessageRegistrationRejection(kind,
					topic,
					ProtocolErrorReasons.MessagingTopicHandled,
					owner.Id));
				continue;
			}

			owners[topic] = state;
			claimed.Add(topic);
		}

		return [.. claimed];
	}

	private void ReleaseClaims(Participant state)
	{
		foreach (var topic in state.Commands)
		{
			if (_commandOwners.TryGetValue(topic, out var owner) && ReferenceEquals(owner, state))
			{
				_commandOwners.Remove(topic);
			}
		}

		foreach (var topic in state.Requests)
		{
			if (_requestOwners.TryGetValue(topic, out var owner) && ReferenceEquals(owner, state))
			{
				_requestOwners.Remove(topic);
			}
		}

		state.Commands = [];
		state.Requests = [];
	}

	private static void EnsureValid(string topic, JsonElement? payload)
	{
		if (!MessageTopic.IsValidTopic(topic))
		{
			throw new MessageChannelException(MessageChannelErrorCode.InvalidTopic, topic, $"'{topic}' is not a valid topic.");
		}

		if (payload is { } element && Encoding.UTF8.GetByteCount(element.GetRawText()) > MessagingLimits.MaxPayloadBytes)
		{
			throw new MessageChannelException(MessageChannelErrorCode.PayloadTooLarge,
				topic,
				$"The payload exceeds {MessagingLimits.MaxPayloadBytes} bytes.");
		}
	}

	private MessageDelivery NewMessage(string senderId, string topic, JsonElement? payload)
		=> new(topic, senderId, Guid.CreateVersion7().ToString(), _timeProvider.GetUtcNow(), payload?.Clone());

	private sealed class Participant : IDisposable
	{
		private readonly MessageBroker _broker;
		private readonly Channel<MessageDelivery> _events = Channel.CreateBounded<MessageDelivery>(
			new BoundedChannelOptions(EventQueueCapacity)
			{
				SingleReader = true,
				FullMode = BoundedChannelFullMode.DropWrite
			});
		private readonly CancellationTokenSource _stopping = new();
		private readonly Lock _dropLock = new();

		private int _handlersInFlight;
		private long _dropped;
		private DateTimeOffset _lastDropLog = DateTimeOffset.MinValue;

		public Participant(string id, string registrationKey, MessageBroker broker)
		{
			Id = id;
			RegistrationKey = registrationKey;
			_broker = broker;
			_ = Task.Run(DrainAsync);
		}

		public string Id { get; }

		public string RegistrationKey { get; }

		public IMessageParticipant Endpoint { get; set; } = null!;

		public string[] Patterns { get; set; } = [];

		public string[] Commands { get; set; } = [];

		public string[] Requests { get; set; } = [];

		public CancellationToken Stopping => _stopping.Token;

		public void Enqueue(MessageDelivery message)
		{
			if (!_events.Writer.TryWrite(message))
			{
				RecordDrop(message.Topic);
			}
		}

		public bool TryEnterHandler()
		{
			if (Interlocked.Increment(ref _handlersInFlight) <= MaxHandlerDeliveriesInFlight)
			{
				return true;
			}

			Interlocked.Decrement(ref _handlersInFlight);
			return false;
		}

		public void LeaveHandler() => Interlocked.Decrement(ref _handlersInFlight);

		public void Dispose()
		{
			Stop();
			_stopping.Dispose();
		}

		public void Stop()
		{
			_events.Writer.TryComplete();
			try
			{
				_stopping.Cancel();
			}
			catch (ObjectDisposedException)
			{
			}
		}

		private async Task DrainAsync()
		{
			try
			{
				await foreach (var message in _events.Reader.ReadAllAsync(_stopping.Token).ConfigureAwait(false))
				{
					try
					{
						await Endpoint.DeliverEventAsync(message, _stopping.Token).ConfigureAwait(false);
					}
					catch (Exception exception) when (exception is not OutOfMemoryException &&
						!_stopping.IsCancellationRequested)
					{
						_broker._logger.Debug(exception,
							"Delivering event {Topic} to {ParticipantId} failed",
							message.Topic,
							Id);
					}
				}
			}
			catch (OperationCanceledException)
			{
			}
		}

		private void RecordDrop(string topic)
		{
			long total;
			var shouldLog = false;

			lock (_dropLock)
			{
				total = ++_dropped;
				var now = _broker._timeProvider.GetUtcNow();
				if (now - _lastDropLog >= _dropLogInterval)
				{
					_lastDropLog = now;
					shouldLog = true;
				}
			}

			if (shouldLog)
			{
				_broker._logger.Warning(
					"The event queue of {ParticipantId} is full; dropped {Topic} ({DroppedTotal} dropped in total)",
					Id,
					topic,
					total);
			}
		}
	}
}
