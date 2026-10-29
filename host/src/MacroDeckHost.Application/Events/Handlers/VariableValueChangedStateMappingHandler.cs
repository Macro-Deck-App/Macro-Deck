using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class VariableValueChangedStateMappingHandler :
	INotificationHandler<VariableValueChangedNotification>,
	INotificationHandler<VariableCreatedNotification>,
	INotificationHandler<VariableDeletedNotification>,
	INotificationHandler<VariableUpdatedNotification>
{
	private readonly IWidgetVariableIndex _index;
	private readonly WidgetStateEvalChannel _evalQueue;

	public VariableValueChangedStateMappingHandler(IWidgetVariableIndex index, WidgetStateEvalChannel evalQueue)
	{
		_index = index;
		_evalQueue = evalQueue;
	}

	public ValueTask Handle(VariableValueChangedNotification notification, CancellationToken cancellationToken)
	{
		Reevaluate(notification.Variable, notification.Variable.Name);
		return ValueTask.CompletedTask;
	}

	public ValueTask Handle(VariableCreatedNotification notification, CancellationToken cancellationToken)
	{
		Reevaluate(notification.Variable, notification.Variable.Name);
		return ValueTask.CompletedTask;
	}

	public ValueTask Handle(VariableDeletedNotification notification, CancellationToken cancellationToken)
	{
		Reevaluate(notification.Variable, notification.Variable.Name);
		return ValueTask.CompletedTask;
	}

	// A value change publishes VariableValueChangedNotification as well, so only a rename is handled here.
	public ValueTask Handle(VariableUpdatedNotification notification, CancellationToken cancellationToken)
	{
		if (notification.PreviousName is { } previousName)
		{
			Reevaluate(notification.Variable, previousName);
			Reevaluate(notification.Variable, notification.Variable.Name);
		}

		return ValueTask.CompletedTask;
	}

	private void Reevaluate(VariableEntity variable, string name)
	{
		if (variable.Scope is VariableScope.Script or VariableScope.Automation)
		{
			return;
		}

		if (variable.Scope == VariableScope.Widget)
		{
			// A button's own state variables are the *output* of resolving it, never an input. Without
			// this, a mapping rule that reads vars.state re-enqueues the widget every time the
			// reconciler writes it, and the button alternates forever at the debounce interval. The
			// depth bound in the reconciler cannot catch it: this path goes back through the queue, so
			// each pass is a fresh top-level reconcile rather than a nested one.
			if (name is WidgetStateReconciler.StateVariableName or WidgetStateReconciler.StateLabelVariableName)
			{
				return;
			}

			if (Guid.TryParse(variable.ScopeRefId, out var ownerId) && _index.StateMappingReferences(ownerId, name))
			{
				_evalQueue.Enqueue(ownerId);
			}

			return;
		}

		foreach (var widgetId in _index.FindStateMappingReferences(name))
		{
			_evalQueue.Enqueue(widgetId);
		}
	}
}
