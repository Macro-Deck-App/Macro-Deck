using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using Serilog;

namespace MacroDeckHost.Application.Triggers;

public sealed class IntegrationEventPublisher : IEventPublisher
{
	private readonly string _integrationId;
	private readonly IEventBus _bus;
	private readonly ILogger _logger;

	public IntegrationEventPublisher(string integrationId, IEventBus bus, ILogger logger)
	{
		_integrationId = integrationId;
		_bus = bus;
		_logger = logger.ForContext<IntegrationEventPublisher>();
	}

	public void Publish(string eventId, IReadOnlyDictionary<string, object?>? parameters = null)
	{
		if (string.IsNullOrWhiteSpace(eventId))
		{
			return;
		}

		if (!QualifiedId.TryCreate(_integrationId, eventId, out var id))
		{
			_logger.Warning(
				"Integration {IntegrationId} published an invalid event id {EventId}; dropping the occurrence",
				_integrationId,
				eventId);
			return;
		}

		_bus.Publish(new EventOccurrence(id.ToString(), parameters ?? EventOccurrence.NoParameters));
	}
}
