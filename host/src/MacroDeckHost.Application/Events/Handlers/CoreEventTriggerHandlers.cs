using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using Mediator;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class VariableValueChangedEventTriggerHandler
	: INotificationHandler<VariableValueChangedNotification>
{
	private readonly IEventBus _bus;

	public VariableValueChangedEventTriggerHandler(IEventBus bus)
	{
		_bus = bus;
	}

	public ValueTask Handle(VariableValueChangedNotification notification, CancellationToken cancellationToken)
	{
		_bus.Publish(new EventOccurrence(EventIds.Qualify(EventIds.VariableChanged),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["variable"] = notification.Variable.Name,
				["value"] = ActionConditionValue(notification.Variable),
				["previousValue"] = notification.PreviousValue
			}));

		return ValueTask.CompletedTask;
	}

	private static object? ActionConditionValue(VariableEntity variable)
		=> ActionConditionEvaluator.ConvertVariableValue(variable);
}

public sealed class IntegrationStateChangedEventTriggerHandler
	: INotificationHandler<IntegrationStateChangedNotification>
{
	private readonly IEventBus _bus;
	private readonly IIntegrationRegistry _integrations;
	private readonly StartupReadiness _readiness;
	private readonly IHostApplicationLifetime _lifetime;

	public IntegrationStateChangedEventTriggerHandler(
		IEventBus bus,
		IIntegrationRegistry integrations,
		StartupReadiness readiness,
		IHostApplicationLifetime lifetime)
	{
		_bus = bus;
		_integrations = integrations;
		_readiness = readiness;
		_lifetime = lifetime;
	}

	public ValueTask Handle(IntegrationStateChangedNotification notification, CancellationToken cancellationToken)
	{
		var integration = _integrations.Integrations.FirstOrDefault(i => i.Id == notification.IntegrationId);
		var enabled = _integrations.IsEnabled(notification.IntegrationId);

		var occurrence = new EventOccurrence(
			EventIds.Qualify(enabled ? EventIds.IntegrationConnected : EventIds.IntegrationDisconnected),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["integrationId"] = notification.IntegrationId,
				["integrationName"] = integration?.Name ?? notification.IntegrationId
			});

		if (_readiness.WhenEventDispatchReady.IsCompletedSuccessfully)
		{
			_bus.Publish(occurrence);
		}
		else
		{
			_ = PublishWhenDispatchReady(occurrence);
		}

		return ValueTask.CompletedTask;
	}

	private async Task PublishWhenDispatchReady(EventOccurrence occurrence)
	{
		try
		{
			// Integrations initialize before the dispatcher has indexed the automations, and the bus drops
			// events nobody subscribes to yet.
			await _readiness.WhenEventDispatchReady.WaitAsync(_lifetime.ApplicationStopping);
			_bus.Publish(occurrence);
		}
		catch (OperationCanceledException)
		{
		}
	}
}

public sealed class VariableCreatedEventTriggerHandler : INotificationHandler<VariableCreatedNotification>
{
	private readonly IEventBus _bus;

	public VariableCreatedEventTriggerHandler(IEventBus bus)
	{
		_bus = bus;
	}

	public ValueTask Handle(VariableCreatedNotification notification, CancellationToken cancellationToken)
	{
		_bus.Publish(new EventOccurrence(EventIds.Qualify(EventIds.VariableCreated),
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["variable"] = notification.Variable.Name }));

		return ValueTask.CompletedTask;
	}
}

public sealed class VariableDeletedEventTriggerHandler : INotificationHandler<VariableDeletedNotification>
{
	private readonly IEventBus _bus;

	public VariableDeletedEventTriggerHandler(IEventBus bus)
	{
		_bus = bus;
	}

	public ValueTask Handle(VariableDeletedNotification notification, CancellationToken cancellationToken)
	{
		_bus.Publish(new EventOccurrence(EventIds.Qualify(EventIds.VariableDeleted),
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["variable"] = notification.Variable.Name }));

		return ValueTask.CompletedTask;
	}
}

public sealed class ProfileCreatedEventTriggerHandler : INotificationHandler<ProfileCreatedNotification>
{
	private readonly IEventBus _bus;

	public ProfileCreatedEventTriggerHandler(IEventBus bus)
	{
		_bus = bus;
	}

	public ValueTask Handle(ProfileCreatedNotification notification, CancellationToken cancellationToken)
	{
		_bus.Publish(ProfileEventTriggerPayload.Occurrence(notification.Profile.Id.ToString(),
			notification.Profile.Name,
			"created"));

		return ValueTask.CompletedTask;
	}
}

public sealed class ProfileUpdatedEventTriggerHandler : INotificationHandler<ProfileUpdatedNotification>
{
	private readonly IEventBus _bus;

	public ProfileUpdatedEventTriggerHandler(IEventBus bus)
	{
		_bus = bus;
	}

	public ValueTask Handle(ProfileUpdatedNotification notification, CancellationToken cancellationToken)
	{
		_bus.Publish(ProfileEventTriggerPayload.Occurrence(notification.Profile.Id.ToString(),
			notification.Profile.Name,
			"updated"));

		return ValueTask.CompletedTask;
	}
}

public sealed class ProfileDeletedEventTriggerHandler : INotificationHandler<ProfileDeletedNotification>
{
	private readonly IEventBus _bus;

	public ProfileDeletedEventTriggerHandler(IEventBus bus)
	{
		_bus = bus;
	}

	public ValueTask Handle(ProfileDeletedNotification notification, CancellationToken cancellationToken)
	{
		_bus.Publish(ProfileEventTriggerPayload.Occurrence(notification.ProfileId.ToString(), string.Empty, "deleted"));

		return ValueTask.CompletedTask;
	}
}

internal static class ProfileEventTriggerPayload
{
	public static EventOccurrence Occurrence(string profileId, string profileName, string change)
		=> new(EventIds.Qualify(EventIds.ProfileChanged),
			new Dictionary<string, object?>(StringComparer.Ordinal)
			{
				["profileId"] = profileId,
				["profileName"] = profileName,
				["change"] = change
			});
}
