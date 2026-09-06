using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Application.Weather;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IntegrationStateChangedNotificationHandler
	: INotificationHandler<IntegrationStateChangedNotification>
{
	private readonly IWeatherBroadcastTrigger _weatherTrigger;
	private readonly IIntegrationIssueBroadcastTrigger _issueTrigger;
	private readonly IVariablePollingInvalidationSignal _variablePollingInvalidation;
	private readonly IVariableSubscriptionCoordinator _variableSubscriptions;
	private readonly IWidgetVariableIndex _widgetVariableIndex;
	private readonly WidgetStateEvalChannel _evalQueue;

	public IntegrationStateChangedNotificationHandler(
		IWeatherBroadcastTrigger weatherTrigger,
		IIntegrationIssueBroadcastTrigger issueTrigger,
		IVariablePollingInvalidationSignal variablePollingInvalidation,
		IVariableSubscriptionCoordinator variableSubscriptions,
		IWidgetVariableIndex widgetVariableIndex,
		WidgetStateEvalChannel evalQueue)
	{
		_weatherTrigger = weatherTrigger;
		_issueTrigger = issueTrigger;
		_variablePollingInvalidation = variablePollingInvalidation;
		_variableSubscriptions = variableSubscriptions;
		_widgetVariableIndex = widgetVariableIndex;
		_evalQueue = evalQueue;
	}

	public async ValueTask Handle(IntegrationStateChangedNotification notification, CancellationToken cancellationToken)
	{
		_weatherTrigger.RequestRefresh();
		_issueTrigger.RequestRefresh();
		_variablePollingInvalidation.MarkStale(notification.IntegrationId);

		// An integration going away must leave its catalog bindings unavailable without user
		// action, and one coming back must resubscribe the same way - this is the only host-observable
		// signal that either just happened.
		await _variableSubscriptions.ReconcileAsync(cancellationToken);

		// A provider-bound button has no variable to react to - an enabled/disabled flip on its
		// integration is the only host-observable signal that its state may just have changed, so its
		// followers are re-queued here instead of waiting for the next poll tick.
		foreach (var widgetId in _widgetVariableIndex.FindProviderReferences(notification.IntegrationId))
		{
			_evalQueue.Enqueue(widgetId);
		}
	}
}
