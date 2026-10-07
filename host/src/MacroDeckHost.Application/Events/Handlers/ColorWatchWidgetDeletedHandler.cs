using MacroDeckHost.Application.Variables.Colors;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

// A colour watched in a widget's scope resolves to nothing once the widget is gone. Deleting a profile
// publishes no per-widget deletion, so it counts as one too.
public sealed class ColorWatchWidgetDeletedHandler(ColorChangeSignal signal) :
	INotificationHandler<WidgetDeletedNotification>,
	INotificationHandler<WidgetsDeletedNotification>,
	INotificationHandler<ProfileDeletedNotification>
{
	public ValueTask Handle(WidgetDeletedNotification notification, CancellationToken cancellationToken)
		=> Raise();

	public ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
		=> Raise();

	public ValueTask Handle(ProfileDeletedNotification notification, CancellationToken cancellationToken)
		=> Raise();

	private ValueTask Raise()
	{
		signal.Raise();
		return ValueTask.CompletedTask;
	}
}
