using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Limits;
using MacroDeck.Sdk.Identity;
using MacroDeck.Sdk.VideoStreams;
using Serilog;

namespace MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;

internal sealed class VideoStreamProviderRegistry : IDisposable
{
	private const int MaxRateLimitedAttempts = 5;

	private static readonly TimeSpan _firstRateLimitBackoff = TimeSpan.FromMilliseconds(100);

	private readonly Lock _gate = new();
	private readonly IHostInvoker _invoker;
	private readonly ILogger _logger;
	private readonly PluginMetadata _metadata;
	private readonly Dictionary<string, Registration> _providers = new(StringComparer.Ordinal);
	private readonly Dictionary<string, SendLane> _sendLanes = new(StringComparer.Ordinal);
	private readonly HashSet<string> _unsentCloses = new(StringComparer.Ordinal);
	private readonly PluginConnectionState _state;
	private readonly TimeProvider _time;

	public VideoStreamProviderRegistry(IHostInvoker invoker,
		PluginConnectionState state,
		PluginMetadata metadata,
		TimeProvider timeProvider,
		ILogger logger)
	{
		_invoker = invoker;
		_state = state;
		_metadata = metadata;
		_time = timeProvider;
		_logger = logger.ForContext<VideoStreamProviderRegistry>();
		Sessions = new VideoStreamSessionTable<VideoStreamSessionReason, IVideoStreamProvider>(timeProvider,
			ProtocolTimeouts.CapabilityInvoke);
		_state.Connected += OnConnected;
		_state.ConnectionEnded += OnConnectionEnded;
	}

	public VideoStreamSessionTable<VideoStreamSessionReason, IVideoStreamProvider> Sessions { get; }

	public void Dispose()
	{
		_state.Connected -= OnConnected;
		_state.ConnectionEnded -= OnConnectionEnded;
	}

	public IVideoStreamProviderContext ContextFor(object? owner)
		=> new Integrations.HostApis.RemoteVideoStreamProviderContext(this, owner);

	public string QualifiedIdFor(string providerId) => _metadata.Id + QualifiedId.Separator + providerId;

	public IReadOnlyList<Registration> Snapshot()
	{
		lock (_gate)
		{
			return [.. _providers.Values];
		}
	}

	public bool TryGet(string? providerId, out Registration registration)
	{
		lock (_gate)
		{
			if (providerId is not null && _providers.TryGetValue(providerId, out var found))
			{
				registration = found;
				return true;
			}

			registration = null!;
			return false;
		}
	}

	public bool IsCurrent(Registration registration)
		=> TryGet(registration.ProviderId, out var current) && ReferenceEquals(current, registration);

	public Registration Add(IVideoStreamProvider provider, object? owner)
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
					$"A plugin can register at most {VideoStreamLimits.MaxProvidersPerPlugin} video stream providers.",
					nameof(provider));
			}

			var registration = new Registration(id, provider, Guid.NewGuid().ToString(), owner);
			_providers[id] = registration;
			return registration;
		}
	}

	public Task UnregisterAsync(string providerId, CancellationToken cancellationToken)
		=> WithdrawAsync(registration => string.Equals(registration.ProviderId, providerId, StringComparison.Ordinal),
			notifyHost: true,
			cancellationToken);

	public Task WithdrawAsync(Registration registration, bool notifyHost)
		=> WithdrawAsync(candidate => ReferenceEquals(candidate, registration), notifyHost, CancellationToken.None);

	public Task ReleaseAsync(object owner, bool notifyHost)
		=> WithdrawAsync(registration => ReferenceEquals(registration.Owner, owner), notifyHost, CancellationToken.None);

	public Task ReleaseAllAsync(bool notifyHost) => WithdrawAsync(_ => true, notifyHost, CancellationToken.None);

	public async Task SendToSessionAsync(string sessionId,
		string operation,
		object arguments,
		CancellationToken cancellationToken)
	{
		SendLane lane;
		lock (_gate)
		{
			if (!_sendLanes.TryGetValue(sessionId, out lane!))
			{
				lane = new SendLane();
				_sendLanes[sessionId] = lane;
			}

			lane.Users++;
		}

		try
		{
			await lane.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				await InvokeWithRetryAsync(operation, arguments, ignoreUnknownSession: true, cancellationToken)
					.ConfigureAwait(false);
			}
			finally
			{
				lane.Gate.Release();
			}
		}
		finally
		{
			lock (_gate)
			{
				if (--lane.Users == 0)
				{
					_sendLanes.Remove(sessionId);
					lane.Gate.Dispose();
				}
			}
		}
	}

	public async Task CloseAsync(VideoStreamSessionTable<VideoStreamSessionReason, IVideoStreamProvider>.Closing closing)
	{
		using var timeout = new CancellationTokenSource(ProtocolTimeouts.CapabilityInvoke);
		try
		{
			await Task.Run(() => closing.Provider.CloseAsync(closing.SessionId, closing.Reason, timeout.Token), timeout.Token)
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

	public async Task<bool> InvokeWithRetryAsync(string operation,
		object? arguments,
		bool ignoreUnknownSession,
		CancellationToken cancellationToken)
	{
		var backoff = _firstRateLimitBackoff;
		for (var attempt = 1;; attempt++)
		{
			try
			{
				return await InvokeHostAsync(operation, arguments, ignoreUnknownSession, cancellationToken)
					.ConfigureAwait(false);
			}
			catch (HostInvocationException exception) when (exception.Code == ProtocolErrorCodes.RateLimited)
			{
				if (attempt == MaxRateLimitedAttempts)
				{
					throw new VideoStreamException(VideoStreamErrorCode.Busy,
						$"Macro Deck kept rate limiting '{operation}'.",
						exception);
				}
			}

			await Task.Delay(backoff, _time, cancellationToken).ConfigureAwait(false);
			backoff *= 2;
		}
	}

	public void RememberUnsentClose(string sessionId)
	{
		lock (_gate)
		{
			_unsentCloses.Add(sessionId);
		}
	}

	public bool TakeUnsentClose(string sessionId)
	{
		lock (_gate)
		{
			return _unsentCloses.Remove(sessionId);
		}
	}

	private async Task<bool> InvokeHostAsync(string operation,
		object? arguments,
		bool ignoreUnknownSession,
		CancellationToken cancellationToken)
	{
		try
		{
			await _invoker.InvokeAsync(HostApis.VideoStreams, operation, arguments, cancellationToken)
				.ConfigureAwait(false);
			return true;
		}
		catch (HostInvocationException exception) when (exception.Code == ProtocolErrorCodes.CapabilityUnsupported)
		{
			return false;
		}
		catch (HostInvocationException exception) when (ignoreUnknownSession &&
			string.Equals(exception.Details.GetValueOrDefault("reason"),
				ProtocolErrorReasons.VideoStreamUnknownSession,
				StringComparison.Ordinal))
		{
			return true;
		}
		catch (HostInvocationException exception) when (
			VideoStreamWire.IsVideoStreamReason(exception.Details.GetValueOrDefault("reason")))
		{
			throw new VideoStreamException(
				VideoStreamWire.FromError(exception.Code, exception.Details.GetValueOrDefault("reason")),
				exception.Message,
				exception);
		}
	}

	private void ForgetUnsentCloses()
	{
		lock (_gate)
		{
			_unsentCloses.Clear();
		}
	}

	private Task CloseAllAsync(
		IReadOnlyList<VideoStreamSessionTable<VideoStreamSessionReason, IVideoStreamProvider>.Closing> closings)
		=> Task.WhenAll(closings.Select(CloseAsync));

	private async Task WithdrawAsync(Func<Registration, bool> predicate,
		bool notifyHost,
		CancellationToken cancellationToken)
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

		if (removed.Count == 0)
		{
			return;
		}

		var closings = removed
			.SelectMany(registration => Sessions.CloseProvider(registration.Provider,
				VideoStreamSessionReason.ProviderRemoved))
			.ToList();

		await CloseAllAsync(closings).ConfigureAwait(false);

		if (!notifyHost)
		{
			return;
		}

		try
		{
			foreach (var closing in closings)
			{
				await InvokeWithRetryAsync(HostOperations.VideoStreams.SessionClose,
						new VideoStreamsSessionCloseArguments
						{
							SessionId = closing.SessionId, Reason = nameof(VideoStreamSessionReason.ProviderRemoved)
						},
						ignoreUnknownSession: true,
						cancellationToken)
					.ConfigureAwait(false);
			}

			await InvokeWithRetryAsync(HostOperations.VideoStreams.ProvidersChanged,
					null,
					ignoreUnknownSession: false,
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is HostInvocationException or VideoStreamException)
		{
			// The host re-reads the provider table on its next attach, so a lost notice heals itself.
			_logger.Debug(exception, "Telling the host about withdrawn video stream providers failed");
		}
	}

	private void OnConnected(object? sender, PluginConnectedEventArgs e)
	{
		ForgetUnsentCloses();
		var epoch = Sessions.AdvanceEpoch();
		var stale = Sessions.CloseOlderThan(epoch, VideoStreamSessionReason.HostDisconnected);
		if (stale.Count > 0)
		{
			_ = Task.Run(() => CloseAllAsync(stale));
		}
	}

	private void OnConnectionEnded()
	{
		ForgetUnsentCloses();
		var ended = Sessions.CurrentEpoch;
		_ = Task.Run(() => CloseAllAsync(Sessions.CloseOlderThan(ended + 1, VideoStreamSessionReason.HostDisconnected)));
	}

	private sealed class SendLane
	{
		public SemaphoreSlim Gate { get; } = new(1, 1);

		public int Users { get; set; }
	}

	internal sealed record Registration(
		string ProviderId,
		IVideoStreamProvider Provider,
		string RegistrationId,
		object? Owner);
}
