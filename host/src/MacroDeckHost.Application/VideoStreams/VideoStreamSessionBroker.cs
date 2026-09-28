using System.Collections.Concurrent;
using System.Threading.Channels;
using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Plugins;
using Mediator;
using Serilog;

namespace MacroDeckHost.Application.VideoStreams;

public sealed class VideoStreamSessionBroker : IVideoStreamSessionBroker, IDisposable
{
	public const int MaxSessionsPerConnection = 16;

	public const int MaxTicketsPerConnection = 32;

	public const int MaxBufferedProviderOperations = 64;

	public const int MaxQueuedSignals = 64;

	public const int MaxAcceptedTransports = 16;

	public const int MaxCloseAttempts = 10;

	public static readonly TimeSpan Lease = TimeSpan.FromSeconds(45);

	private static readonly TimeSpan _maxRetryDelay = TimeSpan.FromSeconds(5);
	private static readonly TimeSpan _sweepInterval = TimeSpan.FromSeconds(5);

	private readonly Lock _gate = new();
	private readonly ILogger _logger;
	private readonly TimeSpan _minRetryDelay;
	private readonly Channel<INotification> _outbox =
		Channel.CreateUnbounded<INotification>(new UnboundedChannelOptions { SingleReader = true });

	private readonly ConcurrentDictionary<Task, byte> _pendingCloses = new();
	private readonly IPluginSessionRegistry _pluginSessions;
	private readonly IPublisher _publisher;
	private readonly VideoStreamProviderRegistry _registry;
	private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
	private readonly ITimer _sweep;
	private readonly Dictionary<string, int> _tickets = new(StringComparer.Ordinal);
	private readonly TimeProvider _time;
	private bool _disposed;

	public VideoStreamSessionBroker(VideoStreamProviderRegistry registry,
		IPluginSessionRegistry pluginSessions,
		IPublisher publisher,
		TimeProvider time,
		ILogger logger)
		: this(registry, pluginSessions, publisher, time, logger, TimeSpan.FromMilliseconds(250))
	{
	}

	internal VideoStreamSessionBroker(VideoStreamProviderRegistry registry,
		IPluginSessionRegistry pluginSessions,
		IPublisher publisher,
		TimeProvider time,
		ILogger logger,
		TimeSpan minRetryDelay)
	{
		_registry = registry;
		_pluginSessions = pluginSessions;
		_publisher = publisher;
		_time = time;
		_logger = logger.ForContext<VideoStreamSessionBroker>();
		_minRetryDelay = minRetryDelay;
		_registry.RegistrationChanged += OnRegistrationChanged;
		_sweep = time.CreateTimer(_ => SweepLeases(), null, _sweepInterval, _sweepInterval);
		_ = Task.Run(PumpAsync);
	}

	public VideoStreamOpenTicket OpenSession(string connectionId,
		string providerId,
		string streamId,
		IReadOnlyList<string> acceptedTransports,
		VideoStreamConsumer consumer)
	{
		ArgumentException.ThrowIfNullOrEmpty(connectionId);
		ArgumentNullException.ThrowIfNull(consumer);

		if (acceptedTransports is not { Count: > 0 and <= MaxAcceptedTransports } ||
			!acceptedTransports.All(VideoStreamLimits.IsValidTransport))
		{
			throw Refuse(VideoStreamError.TransportNotAccepted, "At least one valid accepted transport is required.");
		}

		if (!VideoStreamLimits.IsValidStreamId(streamId))
		{
			throw Refuse(VideoStreamError.UnknownStream, "The stream id is not valid.");
		}

		if (!_registry.TryResolve(providerId, out var entry) ||
			!_registry.TryGetEndpoint(entry.OwnerId, out var endpoint, out var pluginSessionId))
		{
			throw Refuse(VideoStreamError.UnknownProvider, "No enabled video stream provider has this id.");
		}

		if (entry.Streams.Count > 0 &&
			!entry.Streams.Any(stream => string.Equals(stream.Id, streamId, StringComparison.Ordinal)))
		{
			throw Refuse(VideoStreamError.UnknownStream, "The provider offers no stream with this id.");
		}

		Session session;
		lock (_gate)
		{
			if (_sessions.Values.Count(candidate => string.Equals(candidate.ConnectionId,
					connectionId,
					StringComparison.Ordinal)) >= MaxSessionsPerConnection)
			{
				throw Refuse(VideoStreamError.SessionLimitReached, "This connection has too many video sessions open.");
			}

			TakeTicket(connectionId);
			session = new Session(Guid.NewGuid().ToString(),
				connectionId,
				entry.OwnerId,
				entry.ProviderId,
				streamId,
				entry.RegistrationId,
				pluginSessionId,
				endpoint) { LeaseExpiresAt = _time.GetUtcNow() + Lease };
			_sessions[session.Id] = session;
		}

		_ = RunOpenAsync(session, acceptedTransports, consumer);
		return new VideoStreamOpenTicket(session.Id, 0, VideoStreamSessionState.Opening);
	}

	public void KeepAliveSession(string connectionId, string sessionId)
	{
		lock (_gate)
		{
			Owned(connectionId, sessionId).LeaseExpiresAt = _time.GetUtcNow() + Lease;
		}
	}

	public void SuspendSession(string connectionId, string sessionId)
	{
		Session session;
		lock (_gate)
		{
			session = Owned(connectionId, sessionId);
			if (session.Phase != SessionPhase.Open)
			{
				return;
			}
		}

		_ = RunSuspendAsync(session);
	}

	public void ResumeSession(string connectionId, string sessionId)
	{
		Session session;
		lock (_gate)
		{
			session = Owned(connectionId, sessionId);
			if (session.Phase != SessionPhase.Open)
			{
				return;
			}

			TakeTicket(connectionId);
		}

		_ = RunResumeAsync(session);
	}

	public void SignalSession(string connectionId, string sessionId, VideoStreamSignal? signal)
	{
		if (signal is null || string.IsNullOrWhiteSpace(signal.Type) || signal.Payload is null)
		{
			throw Refuse(VideoStreamError.Failed, "A signal needs a type and a payload.");
		}

		if (VideoStreamWire.ValidateSignal(signal) is { } problem)
		{
			throw Refuse(VideoStreamError.PayloadTooLarge, problem);
		}

		Session session;
		bool start;
		lock (_gate)
		{
			session = Owned(connectionId, sessionId);
			if (session.Signals.Count >= MaxQueuedSignals)
			{
				throw Refuse(VideoStreamError.Busy, "Too many signals are waiting for this video session.");
			}

			session.Signals.Enqueue(signal);
			start = session.Phase == SessionPhase.Open && !session.Draining;
			session.Draining |= start;
		}

		if (start)
		{
			_ = DrainSignalsAsync(session);
		}
	}

	public void CloseSession(string connectionId, string sessionId)
	{
		Session? send = null;
		lock (_gate)
		{
			if (!_sessions.TryGetValue(sessionId, out var session) ||
				!string.Equals(session.ConnectionId, connectionId, StringComparison.Ordinal))
			{
				return;
			}

			if (MarkClosed(session, VideoStreamSessionReason.ConsumerClosed, null, null) && TakeCloseSend(session))
			{
				send = session;
			}
		}

		if (send is not null)
		{
			StartClose(send);
		}
	}

	public void CloseConnection(string connectionId, VideoStreamSessionReason reason)
		=> CloseWhere(session => string.Equals(session.ConnectionId, connectionId, StringComparison.Ordinal),
			reason,
			notifyProvider: true);

	public void ApplyProviderUpdate(string ownerId,
		string sessionId,
		VideoStreamSessionState state,
		VideoStreamSessionDescription? description,
		VideoStreamSessionReason reason,
		LocalizedText? message)
		=> ApplyProviderOperation(ownerId,
			sessionId,
			new BufferedOperation(new SessionUpdate(state, description, reason, message), null));

	public void ApplyProviderSignal(string ownerId, string sessionId, VideoStreamSignal signal)
		=> ApplyProviderOperation(ownerId, sessionId, new BufferedOperation(null, signal));

	public bool ApplyProviderClose(string ownerId,
		string sessionId,
		VideoStreamSessionReason reason,
		LocalizedText? message)
	{
		lock (_gate)
		{
			if (!_sessions.TryGetValue(sessionId, out var session) ||
				!string.Equals(session.OwnerId, ownerId, StringComparison.Ordinal))
			{
				return false;
			}

			session.CloseSent = true;
			return MarkClosed(session, reason, message, null);
		}
	}

	public void ClosePluginSession(string pluginSessionId, VideoStreamSessionReason reason)
		=> CloseWhere(session => string.Equals(session.PluginSessionId, pluginSessionId, StringComparison.Ordinal),
			reason,
			notifyProvider: false);

	public void CloseOwner(string ownerId, VideoStreamSessionReason reason, bool notifyProvider)
		=> CloseWhere(session => string.Equals(session.OwnerId, ownerId, StringComparison.Ordinal),
			reason,
			notifyProvider);

	public async Task ShutdownAsync(TimeSpan bound)
	{
		CloseWhere(_ => true, VideoStreamSessionReason.HostShutdown, notifyProvider: true);
		try
		{
			await Task.WhenAll(_pendingCloses.Keys).WaitAsync(bound, _time).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Debug(exception, "Not every video stream session was closed before the host stopped");
		}
	}

	public void Dispose()
	{
		lock (_gate)
		{
			_disposed = true;
		}

		_registry.RegistrationChanged -= OnRegistrationChanged;
		_sweep.Dispose();
		_outbox.Writer.TryComplete();
	}

	internal void SweepLeases()
	{
		var now = _time.GetUtcNow();
		CloseWhere(session => session.LeaseExpiresAt <= now, VideoStreamSessionReason.LeaseExpired, notifyProvider: true);
	}

	private void ApplyProviderOperation(string ownerId, string sessionId, BufferedOperation operation)
	{
		Session? send = null;
		lock (_gate)
		{
			if (!_sessions.TryGetValue(sessionId, out var session) ||
				!string.Equals(session.OwnerId, ownerId, StringComparison.Ordinal))
			{
				throw Refuse(VideoStreamError.UnknownSession, "No video session with this id belongs to this provider.");
			}

			if (session.Phase == SessionPhase.Open)
			{
				ApplyLocked(session, operation);
				return;
			}

			if (session.Buffer.Count < MaxBufferedProviderOperations)
			{
				session.Buffer.Add(operation);
				return;
			}

			_logger.Warning("Video session {SessionId} buffered too many provider updates while opening", sessionId);
			if (MarkClosed(session, VideoStreamSessionReason.Failed, null, VideoStreamError.Failed) &&
				TakeCloseSend(session))
			{
				send = session;
			}
		}

		if (send is not null)
		{
			StartClose(send);
		}
	}

	private void ApplyLocked(Session session, BufferedOperation operation)
	{
		if (operation.Update is { } update)
		{
			session.State = update.State;
			session.Description = update.Description ?? session.Description;
			session.Revision++;
			Post(new VideoStreamSessionChangedNotification(session.ConnectionId,
				session.Id,
				session.Revision,
				session.State,
				session.Description,
				update.Reason,
				update.Message));
		}
		else if (operation.Signal is { } signal)
		{
			Post(new VideoStreamSignalNotification(session.ConnectionId, session.Id, signal));
		}
	}

	private async Task RunOpenAsync(Session session, IReadOnlyList<string> accepted, VideoStreamConsumer consumer)
	{
		VideoStreamOpenResult? result = null;
		VideoStreamEndpointException? failure = null;
		try
		{
			result = await session.Endpoint.OpenAsync(session.ProviderId,
					new VideoStreamOpenRequest(session.Id, session.StreamId, accepted, consumer),
					() => IsPhase(session, SessionPhase.Opening),
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (VideoStreamEndpointException exception)
		{
			failure = exception;
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception, "Opening video session {SessionId} failed", session.Id);
			failure = new VideoStreamEndpointException(VideoStreamEndpointFailure.Rejected,
				VideoStreamErrorCode.Failed,
				exception.Message);
		}

		var send = false;
		var drain = false;
		lock (_gate)
		{
			ReleaseTicket(session.ConnectionId);
			if (result is not null)
			{
				(send, drain) = CompleteOpenLocked(session, result);
			}
			else if (failure is not null)
			{
				var timedOut = failure.Failure == VideoStreamEndpointFailure.TimedOut;
				if (session.Phase == SessionPhase.Opening)
				{
					MarkClosed(session, VideoStreamSessionReason.Failed, null, ErrorFor(failure));
				}

				if (timedOut)
				{
					send = TakeCloseSend(session);
				}
				else
				{
					session.CloseSent = true;
				}
			}
		}

		if (send)
		{
			StartClose(session);
		}

		if (drain)
		{
			_ = DrainSignalsAsync(session);
		}
	}

	private (bool Send, bool Drain) CompleteOpenLocked(Session session, VideoStreamOpenResult result)
	{
		if (session.Phase != SessionPhase.Opening)
		{
			return (TakeCloseSend(session), false);
		}

		var current = _registry.CurrentRegistrationId(session.OwnerId, session.ProviderId);
		if (current is not null && !string.Equals(current, result.RegistrationId, StringComparison.Ordinal))
		{
			MarkClosed(session, VideoStreamSessionReason.ProviderRemoved, null, null);
			return (TakeCloseSend(session), false);
		}

		session.RegistrationId = result.RegistrationId;
		session.Phase = SessionPhase.Open;
		session.State = VideoStreamSessionState.Active;
		session.Description = result.Description;
		session.Revision++;
		Post(new VideoStreamSessionChangedNotification(session.ConnectionId,
			session.Id,
			session.Revision,
			session.State,
			session.Description,
			VideoStreamSessionReason.None,
			null));

		foreach (var operation in session.Buffer)
		{
			ApplyLocked(session, operation);
		}

		session.Buffer.Clear();
		var drain = session.Signals.Count > 0 && !session.Draining;
		session.Draining |= drain;
		return (false, drain);
	}

	private async Task RunSuspendAsync(Session session)
	{
		try
		{
			await session.Endpoint.SuspendAsync(session.ProviderId,
					session.Id,
					() => IsPhase(session, SessionPhase.Open),
					CancellationToken.None)
				.ConfigureAwait(false);
			lock (_gate)
			{
				if (session.Phase == SessionPhase.Open && session.State != VideoStreamSessionState.Suspended)
				{
					session.State = VideoStreamSessionState.Suspended;
					session.Revision++;
					Post(new VideoStreamSessionChangedNotification(session.ConnectionId,
						session.Id,
						session.Revision,
						session.State,
						session.Description,
						VideoStreamSessionReason.None,
						null));
				}
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			HandleSessionCallFailure(session, exception);
		}
	}

	private async Task RunResumeAsync(Session session)
	{
		try
		{
			var description = await session.Endpoint.ResumeAsync(session.ProviderId,
					session.Id,
					() => IsPhase(session, SessionPhase.Open),
					CancellationToken.None)
				.ConfigureAwait(false);
			lock (_gate)
			{
				if (session.Phase == SessionPhase.Open)
				{
					session.State = VideoStreamSessionState.Active;
					session.Description = description ?? session.Description;
					session.Revision++;
					Post(new VideoStreamSessionChangedNotification(session.ConnectionId,
						session.Id,
						session.Revision,
						session.State,
						session.Description,
						VideoStreamSessionReason.None,
						null));
				}
			}
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			HandleSessionCallFailure(session, exception);
		}
		finally
		{
			lock (_gate)
			{
				ReleaseTicket(session.ConnectionId);
			}
		}
	}

	private void HandleSessionCallFailure(Session session, Exception exception)
	{
		if (exception is not VideoStreamEndpointException failure)
		{
			_logger.Warning(exception, "A call for video session {SessionId} failed", session.Id);
			return;
		}

		if (failure.Failure is VideoStreamEndpointFailure.Skipped or VideoStreamEndpointFailure.RateLimited ||
			failure.Code is VideoStreamErrorCode.Busy or VideoStreamErrorCode.CapacityReached)
		{
			_logger.Debug("A call for video session {SessionId} was not delivered: {Message}", session.Id, failure.Message);
			return;
		}

		var send = false;
		lock (_gate)
		{
			if (failure.Code is VideoStreamErrorCode.UnknownSession or VideoStreamErrorCode.UnknownProvider)
			{
				session.CloseSent = true;
				MarkClosed(session, VideoStreamSessionReason.ProviderClosed, null, ErrorFor(failure));
			}
			else if (MarkClosed(session, VideoStreamSessionReason.Failed, null, ErrorFor(failure)))
			{
				send = TakeCloseSend(session);
			}
		}

		if (send)
		{
			StartClose(session);
		}
	}

	private async Task DrainSignalsAsync(Session session)
	{
		var delay = _minRetryDelay;
		for (var attempt = 1;; attempt++)
		{
			VideoStreamSignal head;
			lock (_gate)
			{
				if (session.Phase != SessionPhase.Open || session.Signals.Count == 0 || _disposed)
				{
					session.Draining = false;
					return;
				}

				head = session.Signals.Peek();
			}

			try
			{
				var answer = await session.Endpoint.SignalAsync(session.ProviderId,
						session.Id,
						head,
						() => IsPhase(session, SessionPhase.Open),
						CancellationToken.None)
					.ConfigureAwait(false);
				delay = _minRetryDelay;
				lock (_gate)
				{
					Dequeue(session, head);
					if (answer is not null && session.Phase == SessionPhase.Open)
					{
						Post(new VideoStreamSignalNotification(session.ConnectionId, session.Id, answer));
					}
				}
			}
			catch (VideoStreamEndpointException exception) when (
				exception.Failure == VideoStreamEndpointFailure.RateLimited ||
				(exception.Failure == VideoStreamEndpointFailure.Rejected && exception.Code == VideoStreamErrorCode.Busy))
			{
				await Task.Delay(delay, _time).ConfigureAwait(false);
				delay = Next(delay);
			}
			catch (VideoStreamEndpointException exception) when (exception.Failure == VideoStreamEndpointFailure.Skipped)
			{
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Debug(exception, "A signal for video session {SessionId} was dropped", session.Id);
				lock (_gate)
				{
					Dequeue(session, head);
				}
			}
		}
	}

	private static void Dequeue(Session session, VideoStreamSignal head)
	{
		if (session.Signals.TryPeek(out var current) && ReferenceEquals(current, head))
		{
			session.Signals.Dequeue();
		}
	}

	private void CloseWhere(Func<Session, bool> predicate, VideoStreamSessionReason reason, bool notifyProvider)
	{
		var send = new List<Session>();
		lock (_gate)
		{
			foreach (var session in _sessions.Values.Where(predicate).ToList())
			{
				if (!notifyProvider)
				{
					session.CloseSent = true;
				}

				if (MarkClosed(session, reason, null, null) && TakeCloseSend(session))
				{
					send.Add(session);
				}
			}
		}

		foreach (var session in send)
		{
			StartClose(session);
		}
	}

	private void OnRegistrationChanged(VideoStreamRegistrationChange change)
	{
		lock (_gate)
		{
			foreach (var session in _sessions.Values.Where(candidate =>
						string.Equals(candidate.OwnerId, change.OwnerId, StringComparison.Ordinal) &&
						string.Equals(candidate.ProviderId, change.ProviderId, StringComparison.Ordinal) &&
						!string.Equals(candidate.RegistrationId, change.RegistrationId, StringComparison.Ordinal))
					.ToList())
			{
				// An opening session keeps its close owed, so an open that still succeeds is closed once.
				session.CloseSent |= session.Phase == SessionPhase.Open;
				MarkClosed(session, VideoStreamSessionReason.ProviderRemoved, null, null);
			}
		}
	}

	private void StartClose(Session session)
	{
		var task = SendCloseAsync(session);
		_pendingCloses[task] = 0;
		_ = task.ContinueWith(completed => _pendingCloses.TryRemove(completed, out _),
			CancellationToken.None,
			TaskContinuationOptions.ExecuteSynchronously,
			TaskScheduler.Default);
	}

	private async Task SendCloseAsync(Session session)
	{
		var delay = _minRetryDelay;
		for (var attempt = 1;; attempt++)
		{
			try
			{
				await session.Endpoint.CloseAsync(session.ProviderId, session.Id, session.CloseReason, CancellationToken.None)
					.ConfigureAwait(false);
				return;
			}
			catch (VideoStreamEndpointException exception) when (
				(exception.Failure is VideoStreamEndpointFailure.RateLimited or VideoStreamEndpointFailure.TimedOut) &&
				IsPluginSessionCurrent(session))
			{
				if (attempt == MaxCloseAttempts)
				{
					// The plugin still closes the session on its own once its connection ends.
					_logger.Warning(exception,
						"Giving up closing video session {SessionId} at its provider after {Attempts} attempts",
						session.Id,
						attempt);
					return;
				}

				await Task.Delay(delay, _time).ConfigureAwait(false);
				delay = Next(delay);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Debug(exception, "Closing video session {SessionId} at its provider failed", session.Id);
				return;
			}
		}
	}

	private bool IsPluginSessionCurrent(Session session)
	{
		lock (_gate)
		{
			if (_disposed)
			{
				return false;
			}
		}

		return session.PluginSessionId is { } pluginSessionId &&
			_pluginSessions.Snapshot()
				.Any(candidate => string.Equals(candidate.PluginId, session.OwnerId, StringComparison.Ordinal) &&
					string.Equals(candidate.SessionId, pluginSessionId, StringComparison.Ordinal) &&
					candidate.State == PluginSessionState.Connected);
	}

	private bool MarkClosed(Session session,
		VideoStreamSessionReason reason,
		LocalizedText? message,
		VideoStreamError? error)
	{
		if (session.Phase == SessionPhase.Closed)
		{
			return false;
		}

		session.Phase = SessionPhase.Closed;
		session.CloseReason = reason;
		session.Buffer.Clear();
		session.Signals.Clear();
		_sessions.Remove(session.Id);
		Post(new VideoStreamSessionClosedNotification(session.ConnectionId, session.Id, reason, message, error));
		return true;
	}

	private static bool TakeCloseSend(Session session)
	{
		if (session.CloseSent)
		{
			return false;
		}

		session.CloseSent = true;
		return true;
	}

	private bool IsPhase(Session session, SessionPhase phase)
	{
		lock (_gate)
		{
			return session.Phase == phase;
		}
	}

	private Session Owned(string connectionId, string sessionId)
	{
		if (sessionId is not null &&
			_sessions.TryGetValue(sessionId, out var session) &&
			string.Equals(session.ConnectionId, connectionId, StringComparison.Ordinal))
		{
			return session;
		}

		throw Refuse(VideoStreamError.UnknownSession, "This connection has no video session with this id.");
	}

	private void TakeTicket(string connectionId)
	{
		var taken = _tickets.GetValueOrDefault(connectionId);
		if (taken >= MaxTicketsPerConnection)
		{
			throw Refuse(VideoStreamError.Busy, "Too many video sessions are opening or resuming on this connection.");
		}

		_tickets[connectionId] = taken + 1;
	}

	private void ReleaseTicket(string connectionId)
	{
		var taken = _tickets.GetValueOrDefault(connectionId) - 1;
		if (taken <= 0)
		{
			_tickets.Remove(connectionId);
		}
		else
		{
			_tickets[connectionId] = taken;
		}
	}

	private void Post(INotification notification) => _outbox.Writer.TryWrite(notification);

	private async Task PumpAsync()
	{
		await foreach (var notification in _outbox.Reader.ReadAllAsync().ConfigureAwait(false))
		{
			try
			{
				await _publisher.Publish(notification).ConfigureAwait(false);
			}
			catch (Exception exception) when (exception is not OutOfMemoryException)
			{
				_logger.Warning(exception, "Announcing a video session change failed");
			}
		}
	}

	private static TimeSpan Next(TimeSpan delay)
	{
		var doubled = delay + delay;
		return doubled > _maxRetryDelay ? _maxRetryDelay : doubled;
	}

	private static VideoStreamError ErrorFor(VideoStreamEndpointException failure)
		=> failure.Failure switch
		{
			VideoStreamEndpointFailure.Unavailable => VideoStreamError.ProviderUnavailable,
			VideoStreamEndpointFailure.RateLimited => VideoStreamError.Busy,
			VideoStreamEndpointFailure.TimedOut => VideoStreamError.ProviderUnavailable,
			VideoStreamEndpointFailure.Skipped => VideoStreamError.UnknownSession,
			_ => VideoStreamMapping.ToBrokerError(failure.Code)
		};

	private static VideoStreamBrokerException Refuse(VideoStreamError error, string message) => new(error, message);

	private enum SessionPhase
	{
		Opening,
		Open,
		Closed
	}

	private sealed record SessionUpdate(
		VideoStreamSessionState State,
		VideoStreamSessionDescription? Description,
		VideoStreamSessionReason Reason,
		LocalizedText? Message);

	private sealed record BufferedOperation(SessionUpdate? Update, VideoStreamSignal? Signal);

	private sealed class Session(
		string id,
		string connectionId,
		string ownerId,
		string providerId,
		string streamId,
		string registrationId,
		string? pluginSessionId,
		IVideoStreamEndpoint endpoint)
	{
		public string Id { get; } = id;

		public string ConnectionId { get; } = connectionId;

		public string OwnerId { get; } = ownerId;

		public string ProviderId { get; } = providerId;

		public string StreamId { get; } = streamId;

		public string RegistrationId { get; set; } = registrationId;

		public string? PluginSessionId { get; } = pluginSessionId;

		public IVideoStreamEndpoint Endpoint { get; } = endpoint;

		public SessionPhase Phase { get; set; } = SessionPhase.Opening;

		public VideoStreamSessionState State { get; set; } = VideoStreamSessionState.Opening;

		public VideoStreamSessionDescription? Description { get; set; }

		public long Revision { get; set; }

		public DateTimeOffset LeaseExpiresAt { get; set; }

		public bool CloseSent { get; set; }

		public VideoStreamSessionReason CloseReason { get; set; }

		public List<BufferedOperation> Buffer { get; } = [];

		public Queue<VideoStreamSignal> Signals { get; } = new();

		public bool Draining { get; set; }
	}
}
