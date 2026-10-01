using MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.VideoStreams;
using Serilog;

namespace MacroDeckHost.Application.VideoStreams;

internal sealed class InProcessVideoStreamEndpoint : IVideoStreamEndpoint
{
	private readonly Lock _gate = new();
	private readonly ILogger _logger;
	private readonly Dictionary<string, Registration> _providers = new(StringComparer.Ordinal);
	private readonly VideoStreamSessionTable<VideoStreamSessionReason, IVideoStreamProvider> _sessions;
	private readonly TimeProvider _time;
	private readonly TimeSpan _timeout;

	public InProcessVideoStreamEndpoint(TimeProvider time, TimeSpan timeout, ILogger logger)
	{
		_time = time;
		_timeout = timeout;
		_logger = logger;
		_sessions = new VideoStreamSessionTable<VideoStreamSessionReason, IVideoStreamProvider>(time, timeout);
	}

	public int Count
	{
		get
		{
			lock (_gate)
			{
				return _providers.Count;
			}
		}
	}

	public string Add(IVideoStreamProvider provider)
	{
		ArgumentNullException.ThrowIfNull(provider);
		var id = provider.Id;
		if (!MacroDeckId.TryValidateLocalId(id, LocalIdKind.Resource, out var error))
		{
			throw new ArgumentException($"'{id}' is not a valid video stream provider id: {error}", nameof(provider));
		}

		if (provider.Name.IsEmpty)
		{
			throw new ArgumentException($"The name of video stream provider '{id}' must not be empty.", nameof(provider));
		}

		lock (_gate)
		{
			if (_providers.ContainsKey(id))
			{
				throw new ArgumentException($"A video stream provider with id '{id}' is already registered.",
					nameof(provider));
			}

			if (_providers.Count >= VideoStreamLimits.MaxProvidersPerPlugin)
			{
				throw new ArgumentException(
					$"An integration can register at most {VideoStreamLimits.MaxProvidersPerPlugin} video stream providers.",
					nameof(provider));
			}

			var registration = new Registration(id, provider, Guid.NewGuid().ToString());
			_providers[id] = registration;
			return registration.RegistrationId;
		}
	}

	public Task<bool> RemoveAsync(string providerId)
		=> WithdrawAsync(registration => string.Equals(registration.ProviderId, providerId, StringComparison.Ordinal));

	public Task<bool> RemoveAllAsync() => WithdrawAsync(_ => true);

	public bool IsLive(string sessionId)
		=> _sessions.TryGet(sessionId, out var session) && session.Phase != VideoStreamSessionPhase.Closed;

	public bool EndByProvider(string sessionId) => _sessions.EndByProvider(sessionId, out _);

	public Task<IReadOnlyList<VideoStreamProviderInfo>> DescribeAsync(CancellationToken cancellationToken)
	{
		lock (_gate)
		{
			return Task.FromResult<IReadOnlyList<VideoStreamProviderInfo>>(
			[
				.. _providers.Values.Select(registration => new VideoStreamProviderInfo(registration.ProviderId,
					registration.Provider.Name,
					registration.Provider.Description,
					registration.RegistrationId))
			]);
		}
	}

	public async Task<IReadOnlyList<VideoStreamDescriptor>> GetStreamsAsync(string providerId,
		CancellationToken cancellationToken)
	{
		var registration = Resolve(providerId);
		var streams = await CallAsync(token => registration.Provider.GetStreamsAsync(token), cancellationToken)
			.ConfigureAwait(false);
		return streams ?? [];
	}

	public async Task<VideoStreamOpenResult> OpenAsync(string providerId,
		VideoStreamOpenRequest request,
		Func<bool> proceed,
		CancellationToken cancellationToken)
	{
		if (!proceed())
		{
			throw VideoStreamEndpointException.Skipped();
		}

		var registration = Resolve(providerId);
		var sessionId = request.SessionId;
		switch (_sessions.TryBeginOpen(sessionId, registration.ProviderId, registration.Provider, 0))
		{
			case VideoStreamOpenAdmission.Tombstoned:
				throw Rejected(VideoStreamErrorCode.UnknownSession, "The session was closed before it opened.");
			case VideoStreamOpenAdmission.Duplicate:
				throw Rejected(VideoStreamErrorCode.Failed, "A session with this id is already open.");
		}

		if (!IsCurrent(registration))
		{
			_sessions.FailOpen(sessionId);
			throw Rejected(VideoStreamErrorCode.UnknownProvider, $"No video stream provider '{providerId}' is registered.");
		}

		using var cancellation = new CancellationTokenSource();
		var opening = Task.Run(() => registration.Provider.OpenAsync(request, cancellation.Token), CancellationToken.None);

		VideoStreamSessionDescription description;
		try
		{
			description = await opening.WaitAsync(_timeout, _time, cancellationToken).ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is TimeoutException or OperationCanceledException &&
			!opening.IsCompleted)
		{
			_sessions.RequestClose(sessionId, VideoStreamSessionReason.Failed);
			await cancellation.CancelAsync().ConfigureAwait(false);
			_ = CloseLateOpenAsync(opening, sessionId);
			if (exception is OperationCanceledException)
			{
				throw;
			}

			throw new VideoStreamEndpointException(VideoStreamEndpointFailure.TimedOut,
				VideoStreamErrorCode.Failed,
				"The video stream provider did not open the session in time.");
		}
		catch (VideoStreamException exception)
		{
			_sessions.FailOpen(sessionId);
			throw Rejected(exception.ErrorCode, exception.Message);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_sessions.FailOpen(sessionId);
			_logger.Warning(exception, "Video stream provider {ProviderId} failed to open a session", providerId);
			throw Rejected(VideoStreamErrorCode.Failed, "The video stream provider failed.");
		}

		var (problemCode, problem) = Check(description, request.AcceptedTransports);
		var completion = _sessions.CompleteOpen(sessionId);
		switch (completion.Outcome)
		{
			case VideoStreamOpenOutcome.Open when problem is null:
				return new VideoStreamOpenResult(VideoStreamWire.ToDto(description), registration.RegistrationId);

			case VideoStreamOpenOutcome.Open:
				_logger.Warning("Video stream provider {ProviderId} opened a session with an unusable description: {Problem}",
					providerId,
					problem);
				if (_sessions.RequestClose(sessionId, VideoStreamSessionReason.Failed).Close is { } failed)
				{
					await CloseProviderAsync(failed).ConfigureAwait(false);
				}

				throw Rejected(problemCode, problem!);

			case VideoStreamOpenOutcome.CloseNow:
				await CloseProviderAsync(completion.Close!).ConfigureAwait(false);
				throw Rejected(VideoStreamErrorCode.UnknownSession, "The session was closed while it was opening.");

			default:
				throw Rejected(VideoStreamErrorCode.UnknownSession, "The session was closed while it was opening.");
		}
	}

	public async Task SuspendAsync(string providerId,
		string sessionId,
		Func<bool> proceed,
		CancellationToken cancellationToken)
	{
		var provider = ResolveOpenSession(providerId, sessionId, proceed);
		await CallAsync(async token =>
				{
					await provider.SuspendAsync(sessionId, token).ConfigureAwait(false);
					return true;
				},
				cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task<VideoStreamSessionDescriptionDto?> ResumeAsync(string providerId,
		string sessionId,
		Func<bool> proceed,
		CancellationToken cancellationToken)
	{
		var provider = ResolveOpenSession(providerId, sessionId, proceed);
		var description = await CallAsync(token => provider.ResumeAsync(sessionId, token), cancellationToken)
			.ConfigureAwait(false);
		return description is null ? null : VideoStreamWire.ToDto(description);
	}

	public async Task CloseAsync(string providerId,
		string sessionId,
		VideoStreamSessionReason reason,
		CancellationToken cancellationToken)
	{
		if (_sessions.TryGet(sessionId, out var session) &&
			!string.Equals(session.ProviderId, providerId, StringComparison.Ordinal))
		{
			return;
		}

		if (_sessions.RequestClose(sessionId, reason).Close is { } closing)
		{
			await CloseProviderAsync(closing).ConfigureAwait(false);
		}
	}

	private async Task<bool> WithdrawAsync(Func<Registration, bool> predicate)
	{
		List<Registration> removed;
		lock (_gate)
		{
			removed = [.. _providers.Values.Where(predicate)];
			foreach (var registration in removed)
			{
				_providers.Remove(registration.ProviderId);
			}
		}

		var closings = removed
			.SelectMany(registration => _sessions.CloseProvider(registration.Provider,
				VideoStreamSessionReason.ProviderRemoved))
			.ToList();

		await Task.WhenAll(closings.Select(CloseProviderAsync)).ConfigureAwait(false);
		return removed.Count > 0;
	}

	private async Task CloseLateOpenAsync(Task<VideoStreamSessionDescription> opening, string sessionId)
	{
		try
		{
			await opening.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_sessions.FailOpen(sessionId);
			return;
		}

		if (_sessions.CompleteOpen(sessionId).Close is { } closing)
		{
			await CloseProviderAsync(closing).ConfigureAwait(false);
		}
	}

	private async Task CloseProviderAsync(
		VideoStreamSessionTable<VideoStreamSessionReason, IVideoStreamProvider>.Closing closing)
	{
		try
		{
			await CallAsync(async token =>
					{
						await closing.Provider.CloseAsync(closing.SessionId, closing.Reason, token).ConfigureAwait(false);
						return true;
					},
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			_logger.Warning(exception,
				"Closing video stream session {SessionId} of provider {ProviderId} failed",
				closing.SessionId,
				closing.ProviderId);
		}
	}

	private async Task<T> CallAsync<T>(Func<CancellationToken, Task<T>> call, CancellationToken cancellationToken)
	{
		using var cancellation = new CancellationTokenSource();
		var task = Task.Run(() => call(cancellation.Token), CancellationToken.None);
		try
		{
			return await task.WaitAsync(_timeout, _time, cancellationToken).ConfigureAwait(false);
		}
		catch (TimeoutException)
		{
			await cancellation.CancelAsync().ConfigureAwait(false);
			throw new VideoStreamEndpointException(VideoStreamEndpointFailure.TimedOut,
				VideoStreamErrorCode.Failed,
				"The video stream provider did not answer in time.");
		}
		catch (VideoStreamException exception)
		{
			throw Rejected(exception.ErrorCode, exception.Message);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			await cancellation.CancelAsync().ConfigureAwait(false);
			throw;
		}
		catch (Exception exception) when (exception is not (OutOfMemoryException or VideoStreamEndpointException))
		{
			_logger.Warning(exception, "A built-in video stream provider failed");
			throw Rejected(VideoStreamErrorCode.Failed, "The video stream provider failed.");
		}
	}

	private Registration Resolve(string providerId)
	{
		lock (_gate)
		{
			if (_providers.TryGetValue(providerId, out var registration))
			{
				return registration;
			}
		}

		throw Rejected(VideoStreamErrorCode.UnknownProvider, $"No video stream provider '{providerId}' is registered.");
	}

	private bool IsCurrent(Registration registration)
	{
		lock (_gate)
		{
			return _providers.TryGetValue(registration.ProviderId, out var current) &&
				ReferenceEquals(current, registration);
		}
	}

	private IVideoStreamProvider ResolveOpenSession(string providerId, string sessionId, Func<bool> proceed)
	{
		if (!proceed())
		{
			throw VideoStreamEndpointException.Skipped();
		}

		var registration = Resolve(providerId);
		if (!_sessions.TryGet(sessionId, out var session) ||
			session.Phase != VideoStreamSessionPhase.Open ||
			!string.Equals(session.ProviderId, registration.ProviderId, StringComparison.Ordinal))
		{
			throw Rejected(VideoStreamErrorCode.UnknownSession, "The provider has no open session with this id.");
		}

		return registration.Provider;
	}

	private static (VideoStreamErrorCode Code, string? Problem) Check(
		VideoStreamSessionDescription? description,
		IReadOnlyList<string> accepted)
	{
		if (description is null)
		{
			return (VideoStreamErrorCode.Failed, "A session description must not be null.");
		}

		return accepted.Contains(description.Transport, StringComparer.Ordinal)
			? (VideoStreamErrorCode.Failed, null)
			: (VideoStreamErrorCode.TransportNotAccepted,
				$"The provider answered with transport '{description.Transport}', which the consumer does not accept.");
	}

	private static VideoStreamEndpointException Rejected(VideoStreamErrorCode code, string message)
		=> new(VideoStreamEndpointFailure.Rejected, code, message);

	private sealed record Registration(string ProviderId, IVideoStreamProvider Provider, string RegistrationId);
}
