using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class VariableValueChangedNotificationHandler : INotificationHandler<VariableValueChangedNotification>
{
	private readonly IWidgetVariableIndex _index;
	private readonly LabelRenderChannel _renderQueue;
	private readonly LabelSubscriptionTracker _subscriptions;
	private readonly IWidgetRenderSignals _renderSignals;

	public VariableValueChangedNotificationHandler(
		IWidgetVariableIndex index,
		LabelRenderChannel renderQueue,
		LabelSubscriptionTracker subscriptions,
		IWidgetRenderSignals renderSignals)
	{
		_index = index;
		_renderQueue = renderQueue;
		_subscriptions = subscriptions;
		_renderSignals = renderSignals;
	}

	public ValueTask Handle(VariableValueChangedNotification notification, CancellationToken cancellationToken)
	{
		var variable = notification.Variable;

		_renderSignals.RaiseVariableChanged();

		if (variable.Scope == VariableScope.Widget)
		{
			if (Guid.TryParse(variable.ScopeRefId, out var ownerId) &&
				_index.LabelReferences(ownerId, variable.Name) &&
				HasSubscribers(ownerId))
			{
				_renderQueue.Enqueue(ownerId);
			}

			return ValueTask.CompletedTask;
		}

		foreach (var widgetId in _index.FindLabelReferences(variable.Name))
		{
			if (HasSubscribers(widgetId))
			{
				_renderQueue.Enqueue(widgetId);
			}
		}

		return ValueTask.CompletedTask;
	}

	private bool HasSubscribers(Guid widgetId) => _subscriptions.HasAnySubscribers(widgetId.ToString());
}
