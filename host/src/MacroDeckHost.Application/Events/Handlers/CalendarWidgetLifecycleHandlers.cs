using MacroDeckHost.Application.Calendar;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class CalendarWidgetCreatedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetCreatedNotification>
{
	public ValueTask Handle(WidgetCreatedNotification notification, CancellationToken cancellationToken)
		=> lifecycle.ChangedAsync([notification.Widget]);
}

public sealed class CalendarWidgetsCreatedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetsCreatedNotification>
{
	public ValueTask Handle(WidgetsCreatedNotification notification, CancellationToken cancellationToken)
		=> lifecycle.ChangedAsync(notification.Widgets);
}

public sealed class CalendarWidgetUpdatedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetUpdatedNotification>
{
	public ValueTask Handle(WidgetUpdatedNotification notification, CancellationToken cancellationToken)
		=> lifecycle.ChangedAsync([notification.Widget]);
}

public sealed class CalendarWidgetsUpdatedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetsUpdatedNotification>
{
	public ValueTask Handle(WidgetsUpdatedNotification notification, CancellationToken cancellationToken)
		=> lifecycle.ChangedAsync(notification.Widgets);
}

public sealed class CalendarWidgetDeletedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetDeletedNotification>
{
	public ValueTask Handle(WidgetDeletedNotification notification, CancellationToken cancellationToken)
		=> lifecycle.DeletedAsync([notification.WidgetId]);
}

public sealed class CalendarWidgetsDeletedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetsDeletedNotification>
{
	public ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
		=> lifecycle.DeletedAsync(notification.WidgetIds);
}

// A deleted profile takes its folders out of the cache without a deletion notice per widget.
public sealed class CalendarWidgetProfileDeletedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<ProfileDeletedNotification>
{
	public ValueTask Handle(ProfileDeletedNotification notification, CancellationToken cancellationToken)
	{
		lifecycle.ProfileDeleted();
		return ValueTask.CompletedTask;
	}
}
