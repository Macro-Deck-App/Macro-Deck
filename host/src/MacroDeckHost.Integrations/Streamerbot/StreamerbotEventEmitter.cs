using MacroDeckHost.Integrations.Streamerbot.Protocol;
using MacroDeck.Sdk.Events;

namespace MacroDeckHost.Integrations.Streamerbot;

internal sealed class StreamerbotEventEmitter
{
	private readonly IEventPublisher _publisher;

	public StreamerbotEventEmitter(IEventPublisher publisher)
	{
		_publisher = publisher;
	}

	public void PublishConnected() => _publisher.Publish(StreamerbotEventIds.Connected);

	public void PublishDisconnected() => _publisher.Publish(StreamerbotEventIds.Disconnected);

	public void PublishEvent(StreamerbotEventMessage message)
		=> _publisher.Publish(StreamerbotEventIds.Event,
			StreamerbotEventPayload.Build(message.Source, message.Type, message.Data));
}
