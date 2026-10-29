using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class VariableValueChangedNotificationHandler :
	INotificationHandler<VariableValueChangedNotification>,
	INotificationHandler<VariableCreatedNotification>,
	INotificationHandler<VariableDeletedNotification>,
	INotificationHandler<VariableUpdatedNotification>
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
		Refresh(notification.Variable, notification.Variable.Name);
		return ValueTask.CompletedTask;
	}

	public ValueTask Handle(VariableCreatedNotification notification, CancellationToken cancellationToken)
	{
		Refresh(notification.Variable, notification.Variable.Name);
		return ValueTask.CompletedTask;
	}

	public ValueTask Handle(VariableDeletedNotification notification, CancellationToken cancellationToken)
	{
		Refresh(notification.Variable, notification.Variable.Name);
		return ValueTask.CompletedTask;
	}

	// A value change publishes VariableValueChangedNotification as well, so only a rename is handled here.
	public ValueTask Handle(VariableUpdatedNotification notification, CancellationToken cancellationToken)
	{
		if (notification.PreviousName is { } previousName)
		{
			Refresh(notification.Variable, previousName);
			Refresh(notification.Variable, notification.Variable.Name);
		}

		return ValueTask.CompletedTask;
	}

	private void Refresh(VariableEntity variable, string name)
	{
		_renderSignals.RaiseVariableChanged();

		if (variable.Scope is VariableScope.Script or VariableScope.Automation)
		{
			return;
		}

		if (variable.Scope == VariableScope.Widget)
		{
			if (Guid.TryParse(variable.ScopeRefId, out var ownerId)
				&& _index.LabelReferences(ownerId, name)
				&& HasSubscribers(ownerId))
			{
				_renderQueue.Enqueue(ownerId);
			}

			return;
		}

		foreach (var widgetId in _index.FindLabelReferences(name))
		{
			if (HasSubscribers(widgetId))
			{
				_renderQueue.Enqueue(widgetId);
			}
		}
	}

	private bool HasSubscribers(Guid widgetId)
	{
		return _subscriptions.HasAnySubscribers(widgetId.ToString());
	}
}
