using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.HostLocking;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Events;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Identity;
using Serilog;

namespace MacroDeckHost.Application.Ui.Handlers;

public sealed class TriggerEventRequestMessageHandler
	: IUiTransportMessageHandler<TriggerEventRequest, TriggerEventResponse>
{
	private readonly IEventRegistry _registry;
	private readonly IIntegrationRegistry _integrations;
	private readonly IEventBus _bus;
	private readonly IEventSubscriptionIndex _index;
	private readonly IHostLockState _lockState;
	private readonly ILogger _logger;

	public TriggerEventRequestMessageHandler(
		IEventRegistry registry,
		IIntegrationRegistry integrations,
		IEventBus bus,
		IEventSubscriptionIndex index,
		IHostLockState lockState,
		ILogger logger)
	{
		_registry = registry;
		_integrations = integrations;
		_bus = bus;
		_index = index;
		_lockState = lockState;
		_logger = logger.ForContext<TriggerEventRequestMessageHandler>();
	}

	public ValueTask<TriggerEventResponse> Handle(TriggerEventRequest request, CancellationToken cancellationToken)
	{
		if (string.IsNullOrWhiteSpace(request.EventId))
		{
			return Fail("VALIDATION_ERROR", "An event id is required");
		}

		if (_lockState.IsLocked)
		{
			return Fail(ActionExecutionErrorCodes.HostLocked, "The host is locked.");
		}

		if (!QualifiedId.TryParse(request.EventId, out var id))
		{
			return Fail("VALIDATION_ERROR", "An event id must be providerId::eventId");
		}

		var providerId = id.OwnerId;

		if (_integrations.Integrations.Any(i => i.Id == providerId) && !_integrations.IsEnabled(providerId))
		{
			return Fail("INTEGRATION_DISABLED", "The integration is disabled");
		}

		var descriptor = _registry.Find(request.EventId);
		if (descriptor is null)
		{
			return Fail("NOT_FOUND", "Event not found");
		}

		if (descriptor.Definition.DeliveryKind == EventDeliveryKind.Scheduled)
		{
			return Fail("SCHEDULED_EVENT",
				"A scheduled event cannot be triggered by hand - its schedule decides which trigger runs");
		}

		var coerced = ActionParameterLiteralCoercion.Build(descriptor.Definition.PayloadParameters,
			request.Parameters);
		var parameters = coerced.ToDictionary(p => p.Key, p => (object?)p.Value, StringComparer.Ordinal);

		var occurrence = new EventOccurrence(request.EventId, parameters);
		var queued = _index.Find(request.EventId)
			.Count(subscription => !EventSubscriptionMatcher.QuickReject(subscription, occurrence));

		_bus.Publish(occurrence);
		_logger.Information("Developer tools published {EventId} by hand; queued for {QueuedSubscriptions} trigger(s)",
			request.EventId,
			queued);

		return ValueTask.FromResult(new TriggerEventResponse
		{
			Success = true,
			QueuedSubscriptions = queued
		});
	}

	private static ValueTask<TriggerEventResponse> Fail(string code, string message)
		=> ValueTask.FromResult(new TriggerEventResponse
		{
			Success = false,
			Error = new TransportError { Code = code, Message = message }
		});
}
