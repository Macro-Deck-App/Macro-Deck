using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.VideoStreams;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteVideoStreamProviderContext(VideoStreamProviderRegistry registry, object? owner)
	: IVideoStreamProviderContext
{
	public async Task<VideoStreamProviderRegistration> RegisterProviderAsync(
		IVideoStreamProvider provider,
		CancellationToken cancellationToken = default)
	{
		var registration = registry.Add(provider, owner);

		bool supported;
		try
		{
			supported = await registry.InvokeWithRetryAsync(HostOperations.VideoStreams.ProvidersChanged,
					null,
					ignoreUnknownSession: false,
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch
		{
			await registry.WithdrawAsync(registration, notifyHost: false).ConfigureAwait(false);
			throw;
		}

		if (!supported)
		{
			await registry.WithdrawAsync(registration, notifyHost: false).ConfigureAwait(false);
			return new VideoStreamProviderRegistration(string.Empty, string.Empty);
		}

		return new VideoStreamProviderRegistration(registry.QualifiedIdFor(registration.ProviderId),
			registration.ProviderId);
	}

	public Task UnregisterProviderAsync(string providerId, CancellationToken cancellationToken = default)
		=> registry.UnregisterAsync(providerId, cancellationToken);

	public async Task NotifyStreamsChangedAsync(string providerId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(providerId);

		await registry.InvokeWithRetryAsync(HostOperations.VideoStreams.StreamsChanged,
				new VideoStreamsStreamsChangedArguments { ProviderId = providerId },
				ignoreUnknownSession: false,
				cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task UpdateSessionAsync(
		string sessionId,
		VideoStreamSessionState state,
		VideoStreamSessionDescription? description = null,
		VideoStreamSessionReason reason = VideoStreamSessionReason.None,
		LocalizedText? message = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(sessionId);
		if (description is not null && VideoStreamWire.ValidateDescription(description) is { } problem)
		{
			throw new ArgumentException(problem, nameof(description));
		}

		if (!IsLive(sessionId))
		{
			return;
		}

		await registry.SendToSessionAsync(sessionId,
				HostOperations.VideoStreams.SessionUpdate,
				new VideoStreamsSessionUpdateArguments
				{
					SessionId = sessionId,
					State = state.ToString(),
					Description = description is null ? null : VideoStreamWire.ToDto(description),
					Reason = reason.ToString(),
					Message = message
				},
				cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task SendSignalAsync(string sessionId,
		VideoStreamSignal signal,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(sessionId);
		if (VideoStreamWire.ValidateSignal(signal) is { } problem)
		{
			throw new ArgumentException(problem, nameof(signal));
		}

		if (!IsLive(sessionId))
		{
			return;
		}

		await registry.SendToSessionAsync(sessionId,
				HostOperations.VideoStreams.SessionSignal,
				new VideoStreamsSessionSignalArguments { SessionId = sessionId, Signal = VideoStreamWire.ToDto(signal) },
				cancellationToken)
			.ConfigureAwait(false);
	}

	public async Task CloseSessionAsync(
		string sessionId,
		VideoStreamSessionReason reason = VideoStreamSessionReason.ProviderClosed,
		LocalizedText? message = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(sessionId);

		if (!registry.Sessions.EndByProvider(sessionId, out _) && !registry.TakeUnsentClose(sessionId))
		{
			return;
		}

		try
		{
			await registry.SendToSessionAsync(sessionId,
					HostOperations.VideoStreams.SessionClose,
					new VideoStreamsSessionCloseArguments
					{
						SessionId = sessionId, Reason = reason.ToString(), Message = message
					},
					cancellationToken)
				.ConfigureAwait(false);
		}
		catch
		{
			// The session is already retired locally, so only this marker lets a retry reach the host.
			registry.RememberUnsentClose(sessionId);
			throw;
		}
	}

	private bool IsLive(string sessionId)
		=> registry.Sessions.TryGet(sessionId, out var session) && session.Phase != VideoStreamSessionPhase.Closed;
}
