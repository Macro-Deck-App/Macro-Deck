using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ActionButtonStateVariableWidgetsCreatedHandler
	: INotificationHandler<WidgetsCreatedNotification>
{
	private readonly WidgetStateEvalChannel _evalQueue;

	public ActionButtonStateVariableWidgetsCreatedHandler(WidgetStateEvalChannel evalQueue)
	{
		_evalQueue = evalQueue;
	}

	public ValueTask Handle(WidgetsCreatedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widget in notification.Widgets)
		{
			if (widget.Type == WidgetTypeIds.ActionButton)
			{
				_evalQueue.Enqueue(widget.Id);
			}
		}

		return ValueTask.CompletedTask;
	}
}
