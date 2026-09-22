using System.Text;
using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Capabilities.Messaging;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Sdk.Messaging;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteMessageChannel : IMessageChannel, IDisposable
{
	private static readonly TimeSpan[] _retryDelays =
		[TimeSpan.FromMilliseconds(500), TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];

	private static readonly TimeSpan _replyMargin = TimeSpan.FromSeconds(5);

	private readonly IHostInvoker _invoker;
	private readonly PluginConnectionState _state;
	private readonly PluginMetadata _metadata;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger _logger;
	private readonly Lock _lock = new();
	private readonly List<Entry> _entries = [];
	private readonly SemaphoreSlim _syncGate = new(1, 1);

	private long _version;
	private long _syncedVersion = -1;
	private MessagingSubscriptionsResult? _lastResult;
	private bool _hostHasEntries;

	public RemoteMessageChannel(IHostInvoker invoker,
		PluginConnectionState state,
		PluginMetadata metadata,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_invoker = invoker;
		_state = state;
		_metadata = metadata;
		_timeProvider = timeProvider;
		_logger = logger.ForContext<RemoteMessageChannel>();
		Lifecycle = new LifecycleView(this);
		_state.Connected += OnConnected;
	}

	public IMessageChannel Lifecycle { get; }

	public void Dispose()
	{
		_state.Connected -= OnConnected;
		_syncGate.Dispose();
	}

	public Task PublishAsync(string topic, JsonElement? payload = null, CancellationToken cancellationToken = default)
		=> InvokeAsync(HostOperations.Messaging.Publish, topic, payload, null, cancellationToken);

	public Task SendAsync(string topic,
		JsonElement? payload = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
		=> InvokeAsync(HostOperations.Messaging.Send, topic, payload, ValidTimeout(timeout), cancellationToken);

	public async Task<JsonElement?> RequestAsync(string topic,
		JsonElement? payload = null,
		TimeSpan? timeout = null,
		CancellationToken cancellationToken = default)
	{
		var data = await InvokeAsync(HostOperations.Messaging.Request, topic, payload, ValidTimeout(timeout), cancellationToken)
			.ConfigureAwait(false);
		return data?.Deserialize<MessagingReplyPayload>(PluginProtocolJson.Options)?.Payload;
	}

	public Task<IAsyncDisposable> SubscribeAsync(string topicPattern,
		Func<ChannelMessage, CancellationToken, Task> handler,
		CancellationToken cancellationToken = default)
		=> RegisterAsync(ChannelMessageKind.Event, topicPattern, handler, lifecycle: false, cancellationToken);

	public Task<IAsyncDisposable> HandleCommandsAsync(string topic,
		Func<ChannelMessage, CancellationToken, Task> handler,
		CancellationToken cancellationToken = default)
		=> RegisterAsync(ChannelMessageKind.Command, topic, handler, lifecycle: false, cancellationToken);

	public Task<IAsyncDisposable> HandleRequestsAsync(string topic,
		Func<ChannelMessage, CancellationToken, Task<JsonElement?>> handler,
		CancellationToken cancellationToken = default)
		=> RegisterAsync(ChannelMessageKind.Request, topic, handler, lifecycle: false, cancellationToken);

	// Pending entries keep being synced, so the host holds this plugin's claims while its integrations
	// shut down and initialize again; a re-registration during that pass takes the entry over.
	public void ReleaseLifecycleRegistrations()
	{
		lock (_lock)
		{
			foreach (var entry in _entries.Where(entry => entry.Lifecycle))
			{
				entry.PendingRelease = true;
			}
		}
	}

	public async Task CompleteLifecycleReleaseAsync()
	{
		bool removed;
		lock (_lock)
		{
			removed = _entries.RemoveAll(entry => entry.PendingRelease) > 0;
			if (removed)
			{
				_version++;
			}
		}

		if (removed)
		{
			await TrySyncAsync(CancellationToken.None).ConfigureAwait(false);
		}
	}

	public async Task<CapabilityInvocationResult> DeliverAsync(string operation,
		MessagingDeliveryArguments arguments,
		CancellationToken cancellationToken)
	{
		var kind = operation switch
		{
			Protocol.Capabilities.CapabilityOperations.Messaging.Command => ChannelMessageKind.Command,
			Protocol.Capabilities.CapabilityOperations.Messaging.Request => ChannelMessageKind.Request,
			_ => ChannelMessageKind.Event
		};

		var message = new ChannelMessage
		{
			Kind = kind,
			Topic = arguments.Topic,
			Sender = arguments.Sender,
			MessageId = arguments.MessageId,
			SentAt = arguments.SentAt,
			Payload = arguments.Payload
		};

		if (kind == ChannelMessageKind.Event)
		{
			await DeliverEventAsync(message, cancellationToken).ConfigureAwait(false);
			return CapabilityInvocationResult.Ok();
		}

		Entry? handler;
		bool pending;
		lock (_lock)
		{
			var candidates = _entries.Where(entry => entry.Kind == kind &&
				string.Equals(entry.Topic, arguments.Topic, StringComparison.Ordinal)).ToList();
			handler = candidates.FirstOrDefault(entry => !entry.PendingRelease);
			pending = candidates.Count > 0;
		}

		if (handler is null)
		{
			return pending
				? Failure(ProtocolErrorReasons.MessagingHandlerUnavailable, "The handler is being reinitialized.", retryable: true)
				: Failure(ProtocolErrorReasons.MessagingNoHandler, "Nothing in this plugin handles the topic.");
		}

		try
		{
			JsonElement? reply = null;
			if (handler.Handler is Func<ChannelMessage, CancellationToken, Task<JsonElement?>> requestHandler)
			{
				reply = await Task.Run(() => requestHandler(message, cancellationToken), cancellationToken)
					.ConfigureAwait(false);
			}
			else if (handler.Handler is Func<ChannelMessage, CancellationToken, Task> commandHandler)
			{
				await Task.Run(() => commandHandler(message, cancellationToken), cancellationToken).ConfigureAwait(false);
			}

			if (reply is { } element && Encoding.UTF8.GetByteCount(element.GetRawText()) > MessagingLimits.MaxPayloadBytes)
			{
				_logger.Warning("The reply to {Topic} exceeds {MaxBytes} bytes and was not sent",
					arguments.Topic,
					MessagingLimits.MaxPayloadBytes);
				return CapabilityInvocationResult.Failed(ProtocolErrorCodes.PayloadTooLarge,
					"The reply is too large.");
			}

			return kind == ChannelMessageKind.Request
				? CapabilityInvocationResult.Ok(new MessagingDeliveryResult { Payload = reply })
				: CapabilityInvocationResult.Ok();
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "The handler of {Topic} failed", arguments.Topic);
			return Failure(ProtocolErrorReasons.MessagingHandlerFailed, "The handler failed.");
		}
	}

	private async Task DeliverEventAsync(ChannelMessage message, CancellationToken cancellationToken)
	{
		Entry[] matching;
		lock (_lock)
		{
			matching =
			[
				.. _entries.Where(entry => entry.Kind == ChannelMessageKind.Event &&
					!entry.PendingRelease &&
					MessageTopic.Matches(entry.Topic, message.Topic))
			];
		}

		foreach (var entry in matching)
		{
			try
			{
				await Task.Run(() => ((Func<ChannelMessage, CancellationToken, Task>)entry.Handler)(message, cancellationToken),
						cancellationToken)
					.ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
			{
				_logger.Warning(exception, "A subscription to {Pattern} failed on {Topic}", entry.Topic, message.Topic);
			}
		}
	}

	private async Task<IAsyncDisposable> RegisterAsync(ChannelMessageKind kind,
		string topic,
		Delegate handler,
		bool lifecycle,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(handler);

		var valid = kind == ChannelMessageKind.Event ? MessageTopic.IsValidPattern(topic) : MessageTopic.IsValidTopic(topic);
		if (!valid)
		{
			throw new MessageChannelException(MessageChannelErrorCode.InvalidTopic, topic, $"'{topic}' is not a valid topic.");
		}

		ThrowIfUnsupported(topic);

		var entry = new Entry(kind, topic, handler, lifecycle);
		lock (_lock)
		{
			if (kind != ChannelMessageKind.Event)
			{
				var existing = _entries.FirstOrDefault(other => other.Kind == kind &&
					string.Equals(other.Topic, topic, StringComparison.Ordinal));
				if (existing is { PendingRelease: false })
				{
					throw new MessageChannelException(MessageChannelErrorCode.TopicAlreadyHandled,
						topic,
						$"'{topic}' is already handled in this plugin.",
						_metadata.Id);
				}

				if (existing is not null)
				{
					_entries.Remove(existing);
				}
			}

			if (_entries.Count(other => other.Kind == kind) >= MessagingLimits.MaxSubscriptionsPerKind)
			{
				throw new InvalidOperationException(
					$"A plugin can register at most {MessagingLimits.MaxSubscriptionsPerKind} topics of one kind.");
			}

			_entries.Add(entry);
			_version++;
		}

		MessagingSubscriptionsResult? result;
		try
		{
			result = await TrySyncAsync(cancellationToken).ConfigureAwait(false);
		}
		catch
		{
			RemoveEntry(entry);
			throw;
		}

		var rejection = result?.Rejected.FirstOrDefault(rejected => rejected.Kind == KindName(kind) &&
			string.Equals(rejected.Topic, topic, StringComparison.Ordinal));
		if (rejection is not null)
		{
			RemoveEntry(entry);
			throw string.Equals(rejection.Reason, ProtocolErrorReasons.MessagingTopicHandled, StringComparison.Ordinal)
				? new MessageChannelException(MessageChannelErrorCode.TopicAlreadyHandled,
					topic,
					$"'{topic}' is already handled by {rejection.Owner}.",
					rejection.Owner)
				: new MessageChannelException(MessageChannelErrorCode.InvalidTopic, topic, $"'{topic}' is not a valid topic.");
		}

		return new Registration(this, entry);
	}

	private void RemoveEntry(Entry entry)
	{
		lock (_lock)
		{
			if (_entries.Remove(entry))
			{
				_version++;
			}
		}
	}

	private async Task UnregisterAsync(Entry entry)
	{
		lock (_lock)
		{
			if (entry.PendingRelease || !_entries.Remove(entry))
			{
				return;
			}

			_version++;
		}

		await TrySyncAsync(CancellationToken.None).ConfigureAwait(false);
	}

	private void OnConnected(object? sender, PluginConnectedEventArgs e)
	{
		if (!e.Resumed)
		{
			_hostHasEntries = false;
		}

		_ = ResyncAfterConnectAsync();
	}

	private async Task ResyncAfterConnectAsync()
	{
		try
		{
			lock (_lock)
			{
				_syncedVersion = -1;
			}

			var result = await TrySyncAsync(CancellationToken.None).ConfigureAwait(false);
			if (result is null)
			{
				return;
			}

			foreach (var rejected in result.Rejected)
			{
				_logger.Warning("Macro Deck no longer accepts {Kind} topic {Topic} from this plugin ({Reason}, handled by {Owner})",
					rejected.Kind,
					rejected.Topic,
					rejected.Reason,
					rejected.Owner);

				lock (_lock)
				{
					if (_entries.RemoveAll(entry => KindName(entry.Kind) == rejected.Kind &&
						string.Equals(entry.Topic, rejected.Topic, StringComparison.Ordinal)) > 0)
					{
						_version++;
					}
				}
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Debug(exception, "Resyncing message channel registrations failed");
		}
	}

	private async Task<MessagingSubscriptionsResult?> TrySyncAsync(CancellationToken cancellationToken)
	{
		long wanted;
		lock (_lock)
		{
			wanted = _version;
		}

		await _syncGate.WaitAsync(cancellationToken).ConfigureAwait(false);
		try
		{
			MessagingSubscriptionsArguments table;
			long version;
			lock (_lock)
			{
				if (_syncedVersion >= wanted)
				{
					return _lastResult;
				}

				version = _version;
				table = Table();
			}

			var empty = table.Events.Count == 0 && table.Commands.Count == 0 && table.Requests.Count == 0;
			if (_state.ActiveConnection is null || !_state.MessagingAccepted || (empty && !_hostHasEntries))
			{
				return null;
			}

			for (var attempt = 0;; attempt++)
			{
				try
				{
					var data = await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Messaging,
							HostOperations.Messaging.Subscriptions,
							table,
							cancellationToken)
						.ConfigureAwait(false);

					var result = data?.Deserialize<MessagingSubscriptionsResult>(PluginProtocolJson.Options) ??
						new MessagingSubscriptionsResult();
					lock (_lock)
					{
						_syncedVersion = version;
						_lastResult = result;
					}

					_hostHasEntries = !empty;
					return result;
				}
				catch (HostInvocationException exception) when (exception.Retryable &&
					!string.Equals(exception.Code, ProtocolErrorCodes.SessionNotFound, StringComparison.Ordinal) &&
					_state.ActiveConnection is not null &&
					attempt < _retryDelays.Length)
				{
					await Task.Delay(_retryDelays[attempt], _timeProvider, cancellationToken).ConfigureAwait(false);
				}
				catch (HostInvocationException exception) when (IsUnsupported(exception))
				{
					throw new MessageChannelException(MessageChannelErrorCode.Unsupported,
						null,
						"This Macro Deck offers no message channel.");
				}
				catch (HostInvocationException exception)
				{
					_logger.Debug("Message channel registrations stay local until the next connect ({Code})",
						exception.Code);
					return null;
				}
			}
		}
		finally
		{
			_syncGate.Release();
		}
	}

	private MessagingSubscriptionsArguments Table()
		=> new()
		{
			Events =
			[
				.. _entries.Where(entry => entry.Kind == ChannelMessageKind.Event)
					.Select(entry => entry.Topic)
					.Distinct(StringComparer.Ordinal)
			],
			Commands = [.. _entries.Where(entry => entry.Kind == ChannelMessageKind.Command).Select(entry => entry.Topic)],
			Requests = [.. _entries.Where(entry => entry.Kind == ChannelMessageKind.Request).Select(entry => entry.Topic)]
		};

	private async Task<JsonElement?> InvokeAsync(string operation,
		string topic,
		JsonElement? payload,
		TimeSpan? timeout,
		CancellationToken cancellationToken)
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

		if (_state.ActiveConnection is null)
		{
			throw new MessageChannelException(MessageChannelErrorCode.NotConnected,
				topic,
				"There is no connection to Macro Deck.");
		}

		ThrowIfUnsupported(topic);

		var arguments = new MessagingMessageArguments { Topic = topic, Payload = payload };
		try
		{
			return timeout is { } deadline
				? await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Messaging, operation, arguments, deadline, deadline + _replyMargin, cancellationToken)
					.ConfigureAwait(false)
				: await _invoker.InvokeAsync(Protocol.Callbacks.HostApis.Messaging, operation, arguments, cancellationToken)
					.ConfigureAwait(false);
		}
		catch (HostInvocationException exception)
		{
			throw Map(exception, topic);
		}
	}

	private void ThrowIfUnsupported(string topic)
	{
		if (_state.ActiveConnection is not null && !_state.MessagingAccepted)
		{
			throw new MessageChannelException(MessageChannelErrorCode.Unsupported,
				topic,
				"This Macro Deck offers no message channel.");
		}
	}

	private static bool IsUnsupported(HostInvocationException exception)
		=> string.Equals(exception.Code, ProtocolErrorCodes.CapabilityUnsupported, StringComparison.Ordinal) ||
			string.Equals(exception.Details.GetValueOrDefault("reason"), ProtocolErrorReasons.MessagingNotDeclared,
				StringComparison.Ordinal);

	private static MessageChannelException Map(HostInvocationException exception, string topic)
	{
		if (IsUnsupported(exception))
		{
			return new MessageChannelException(MessageChannelErrorCode.Unsupported, topic, "This Macro Deck offers no message channel.");
		}

		var code = exception.Details.GetValueOrDefault("reason") switch
		{
			ProtocolErrorReasons.MessagingInvalidTopic => MessageChannelErrorCode.InvalidTopic,
			ProtocolErrorReasons.MessagingNoHandler => MessageChannelErrorCode.NoHandler,
			ProtocolErrorReasons.MessagingHandlerUnavailable => MessageChannelErrorCode.HandlerUnavailable,
			ProtocolErrorReasons.MessagingHandlerFailed => MessageChannelErrorCode.HandlerFailed,
			_ => exception.Code switch
			{
				ProtocolErrorCodes.PayloadTooLarge => MessageChannelErrorCode.PayloadTooLarge,
				ProtocolErrorCodes.Timeout => MessageChannelErrorCode.Timeout,
				ProtocolErrorCodes.RateLimited => MessageChannelErrorCode.RateLimited,
				ProtocolErrorCodes.CapabilityUnavailable => MessageChannelErrorCode.NotConnected,
				_ => MessageChannelErrorCode.Unknown
			}
		};

		return new MessageChannelException(code, topic, exception.Message);
	}

	private static TimeSpan ValidTimeout(TimeSpan? timeout)
	{
		var value = timeout ?? MessagingLimits.MaxHandlerTimeout;
		ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(value, TimeSpan.Zero, nameof(timeout));
		ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MessagingLimits.MaxHandlerTimeout, nameof(timeout));
		return value;
	}

	private static CapabilityInvocationResult Failure(string reason, string message, bool retryable = false)
		=> CapabilityInvocationResult.Failed(new ProtocolError
		{
			Code = ProtocolErrorCodes.CapabilityUnavailable,
			Message = message,
			Retryable = retryable,
			Details = new Dictionary<string, string>(StringComparer.Ordinal) { ["reason"] = reason }
		});

	private static string KindName(ChannelMessageKind kind)
		=> kind switch
		{
			ChannelMessageKind.Command => "command",
			ChannelMessageKind.Request => "request",
			_ => "event"
		};

	private sealed class Entry(ChannelMessageKind kind, string topic, Delegate handler, bool lifecycle)
	{
		public ChannelMessageKind Kind { get; } = kind;

		public string Topic { get; } = topic;

		public Delegate Handler { get; } = handler;

		public bool Lifecycle { get; } = lifecycle;

		public bool PendingRelease { get; set; }
	}

	private sealed class Registration(RemoteMessageChannel channel, Entry entry) : IAsyncDisposable
	{
		private int _disposed;

		public async ValueTask DisposeAsync()
		{
			if (Interlocked.Exchange(ref _disposed, 1) == 0)
			{
				await channel.UnregisterAsync(entry).ConfigureAwait(false);
			}
		}
	}

	private sealed class LifecycleView(RemoteMessageChannel channel) : IMessageChannel
	{
		public Task PublishAsync(string topic, JsonElement? payload = null, CancellationToken cancellationToken = default)
			=> channel.PublishAsync(topic, payload, cancellationToken);

		public Task SendAsync(string topic,
			JsonElement? payload = null,
			TimeSpan? timeout = null,
			CancellationToken cancellationToken = default)
			=> channel.SendAsync(topic, payload, timeout, cancellationToken);

		public Task<JsonElement?> RequestAsync(string topic,
			JsonElement? payload = null,
			TimeSpan? timeout = null,
			CancellationToken cancellationToken = default)
			=> channel.RequestAsync(topic, payload, timeout, cancellationToken);

		public Task<IAsyncDisposable> SubscribeAsync(string topicPattern,
			Func<ChannelMessage, CancellationToken, Task> handler,
			CancellationToken cancellationToken = default)
			=> channel.RegisterAsync(ChannelMessageKind.Event, topicPattern, handler, lifecycle: true, cancellationToken);

		public Task<IAsyncDisposable> HandleCommandsAsync(string topic,
			Func<ChannelMessage, CancellationToken, Task> handler,
			CancellationToken cancellationToken = default)
			=> channel.RegisterAsync(ChannelMessageKind.Command, topic, handler, lifecycle: true, cancellationToken);

		public Task<IAsyncDisposable> HandleRequestsAsync(string topic,
			Func<ChannelMessage, CancellationToken, Task<JsonElement?>> handler,
			CancellationToken cancellationToken = default)
			=> channel.RegisterAsync(ChannelMessageKind.Request, topic, handler, lifecycle: true, cancellationToken);
	}
}
