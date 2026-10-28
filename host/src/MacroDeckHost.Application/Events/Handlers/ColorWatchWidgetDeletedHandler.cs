using MacroDeckHost.Application.Variables.Colors;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

// A colour watched in a widget's scope resolves to nothing once the widget is gone.
public sealed class ColorWatchWidgetDeletedHandler(ColorChangeSignal signal) :
	INotificationHandler<WidgetDeletedNotification>,
	INotificationHandler<WidgetsDeletedNotification>
{
	public ValueTask Handle(WidgetDeletedNotification notification, CancellationToken cancellationToken)
	{
		return Raise();
	}

	public ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
	{
		return Raise();
	}

	private ValueTask Raise()
	{
		signal.Raise();
		return ValueTask.CompletedTask;
	}
}
