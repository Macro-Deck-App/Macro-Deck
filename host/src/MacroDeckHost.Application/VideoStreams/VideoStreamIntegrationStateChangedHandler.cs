using MacroDeck.Sdk.VideoStreams;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using Mediator;

namespace MacroDeckHost.Application.VideoStreams;

public sealed class VideoStreamIntegrationStateChangedHandler : INotificationHandler<IntegrationStateChangedNotification>
{
	private readonly IVideoStreamSessionBroker _broker;
	private readonly IIntegrationRegistry _integrations;
	private readonly VideoStreamProviderRegistry _registry;

	public VideoStreamIntegrationStateChangedHandler(IIntegrationRegistry integrations,
		VideoStreamProviderRegistry registry,
		IVideoStreamSessionBroker broker)
	{
		_integrations = integrations;
		_registry = registry;
		_broker = broker;
	}

	public async ValueTask Handle(IntegrationStateChangedNotification notification,
		CancellationToken cancellationToken)
	{
		if (!_registry.HasOwner(notification.IntegrationId))
		{
			return;
		}

		if (!_integrations.IsEnabled(notification.IntegrationId))
		{
			_broker.CloseOwner(notification.IntegrationId, VideoStreamSessionReason.ProviderRemoved, notifyProvider: true);
		}

		await _registry.PublishCatalogChangedAsync();
	}
}
