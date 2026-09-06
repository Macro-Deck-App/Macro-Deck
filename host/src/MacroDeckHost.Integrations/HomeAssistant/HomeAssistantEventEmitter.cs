using MacroDeckHost.Integrations.HomeAssistant.Protocol;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.HomeAssistant;

internal sealed class HomeAssistantEventEmitter
{
	private readonly IEventPublisher _publisher;

	public HomeAssistantEventEmitter(IEventPublisher publisher)
	{
		_publisher = publisher;
	}

	public void PublishConnected() => _publisher.Publish(HomeAssistantEventIds.Connected);

	public void PublishDisconnected() => _publisher.Publish(HomeAssistantEventIds.Disconnected);

	public void PublishStateChanged(
		string entityId,
		HomeAssistantEntityState? newState,
		string? fromState,
		string? area)
		=> _publisher.Publish(HomeAssistantEventIds.EntityStateChanged,
			HomeAssistantEventPayload.StateChanged(entityId, newState, fromState, area));

	public void PublishEvent(HomeAssistantEventMessage message)
		=> _publisher.Publish(HomeAssistantEventIds.Event,
			HomeAssistantEventPayload.Event(message.EventType, message.Data));
}
