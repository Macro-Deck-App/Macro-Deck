using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ActionButtonStateVariableWidgetUpdatedHandler
	: INotificationHandler<WidgetUpdatedNotification>
{
	private readonly WidgetStateEvalChannel _evalQueue;
	private readonly WidgetOptimisticStateStore? _optimisticStates;

	public ActionButtonStateVariableWidgetUpdatedHandler(
		WidgetStateEvalChannel evalQueue,
		WidgetOptimisticStateStore? optimisticStates = null)
	{
		_evalQueue = evalQueue;
		_optimisticStates = optimisticStates;
	}

	public ValueTask Handle(WidgetUpdatedNotification notification, CancellationToken cancellationToken)
	{
		if (notification.Widget.Type == WidgetTypeIds.ActionButton)
		{
			_optimisticStates?.ClearWidget(notification.Widget.Id);
			_evalQueue.Enqueue(notification.Widget.Id);
		}

		return ValueTask.CompletedTask;
	}
}
