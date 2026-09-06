using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Domain.Enums;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class VariableValueChangedStateMappingHandler
	: INotificationHandler<VariableValueChangedNotification>
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
		var variable = notification.Variable;

		if (variable.Scope == VariableScope.Widget)
		{
			// A button's own state variables are the *output* of resolving it, never an input. Without
			// this, a mapping rule that reads vars.state re-enqueues the widget every time the
			// reconciler writes it, and the button alternates forever at the debounce interval. The
			// depth bound in the reconciler cannot catch it: this path goes back through the queue, so
			// each pass is a fresh top-level reconcile rather than a nested one.
			if (variable.Name is WidgetStateReconciler.StateVariableName
				or WidgetStateReconciler.StateLabelVariableName)
			{
				return ValueTask.CompletedTask;
			}

			if (Guid.TryParse(variable.ScopeRefId, out var ownerId) &&
				_index.StateMappingReferences(ownerId, variable.Name))
			{
				_evalQueue.Enqueue(ownerId);
			}

			return ValueTask.CompletedTask;
		}

		foreach (var widgetId in _index.FindStateMappingReferences(variable.Name))
		{
			_evalQueue.Enqueue(widgetId);
		}

		return ValueTask.CompletedTask;
	}
}
