using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ActionButtonStateVariableWidgetsUpdatedHandler
	: INotificationHandler<WidgetsUpdatedNotification>
{
	private readonly WidgetStateEvalChannel _evalQueue;
	private readonly WidgetOptimisticStateStore? _optimisticStates;

	public ActionButtonStateVariableWidgetsUpdatedHandler(
		WidgetStateEvalChannel evalQueue,
		WidgetOptimisticStateStore? optimisticStates = null)
	{
		_evalQueue = evalQueue;
		_optimisticStates = optimisticStates;
	}

	public ValueTask Handle(WidgetsUpdatedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widget in notification.Widgets)
		{
			if (widget.Type == WidgetTypeIds.ActionButton)
			{
				_optimisticStates?.ClearWidget(widget.Id);
				_evalQueue.Enqueue(widget.Id);
			}
		}

		return ValueTask.CompletedTask;
	}
}
