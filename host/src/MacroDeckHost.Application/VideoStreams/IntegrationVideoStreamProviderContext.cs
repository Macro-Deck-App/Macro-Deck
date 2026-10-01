using MacroDeck.Localization;
using MacroDeck.Plugin.Hosting.Capabilities.VideoStreamProvider;
using MacroDeck.Sdk.VideoStreams;

namespace MacroDeckHost.Application.VideoStreams;

public sealed class IntegrationVideoStreamProviderContext : IVideoStreamProviderContext
{
	private readonly IVideoStreamSessionBroker _broker;
	private readonly string _integrationId;
	private readonly VideoStreamProviderRegistry _registry;

	public IntegrationVideoStreamProviderContext(string integrationId,
		VideoStreamProviderRegistry registry,
		IVideoStreamSessionBroker broker)
	{
		_integrationId = integrationId;
		_registry = registry;
		_broker = broker;
	}

	public Task<VideoStreamProviderRegistration> RegisterProviderAsync(IVideoStreamProvider provider,
		CancellationToken cancellationToken = default)
		=> _registry.RegisterInProcessAsync(_integrationId, provider);

	public Task UnregisterProviderAsync(string providerId, CancellationToken cancellationToken = default)
		=> _registry.UnregisterInProcessAsync(_integrationId, providerId);

	public Task NotifyStreamsChangedAsync(string providerId, CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(providerId);
		return _registry.RequestStreamsPullAsync(_integrationId, providerId);
	}

	public Task UpdateSessionAsync(string sessionId,
		VideoStreamSessionState state,
		VideoStreamSessionDescription? description = null,
		VideoStreamSessionReason reason = VideoStreamSessionReason.None,
		LocalizedText? message = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(sessionId);
		if (IsLive(sessionId))
		{
			var dto = description is null ? null : VideoStreamWire.ToDto(description);
			Forward(() => _broker.ApplyProviderUpdate(_integrationId, sessionId, state, dto, reason, message));
		}

		return Task.CompletedTask;
	}

	public Task CloseSessionAsync(string sessionId,
		VideoStreamSessionReason reason = VideoStreamSessionReason.ProviderClosed,
		LocalizedText? message = null,
		CancellationToken cancellationToken = default)
	{
		ArgumentException.ThrowIfNullOrEmpty(sessionId);
		if (_registry.InProcessEndpoint(_integrationId) is { } endpoint && endpoint.EndByProvider(sessionId))
		{
			_broker.ApplyProviderClose(_integrationId, sessionId, reason, message);
		}

		return Task.CompletedTask;
	}

	private bool IsLive(string sessionId) => _registry.InProcessEndpoint(_integrationId)?.IsLive(sessionId) == true;

	private static void Forward(Action apply)
	{
		try
		{
			apply();
		}
		catch (VideoStreamBrokerException exception) when (exception.Error == VideoStreamError.UnknownSession)
		{
		}
	}
}
