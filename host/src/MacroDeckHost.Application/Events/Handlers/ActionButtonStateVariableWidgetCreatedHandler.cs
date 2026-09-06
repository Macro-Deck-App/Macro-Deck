using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Domain.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ActionButtonStateVariableWidgetCreatedHandler
	: INotificationHandler<WidgetCreatedNotification>
{
	private readonly WidgetStateEvalChannel _evalQueue;

	public ActionButtonStateVariableWidgetCreatedHandler(WidgetStateEvalChannel evalQueue)
	{
		_evalQueue = evalQueue;
	}

	public ValueTask Handle(WidgetCreatedNotification notification, CancellationToken cancellationToken)
	{
		if (notification.Widget.Type == WidgetTypeIds.ActionButton)
		{
			_evalQueue.Enqueue(notification.Widget.Id);
		}

		return ValueTask.CompletedTask;
	}
}
