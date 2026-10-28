using MacroDeckHost.Application.Calendar;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class CalendarWidgetCreatedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetCreatedNotification>
{
	public ValueTask Handle(WidgetCreatedNotification notification, CancellationToken cancellationToken)
	{
		return lifecycle.ChangedAsync([notification.Widget]);
	}
}

public sealed class CalendarWidgetsCreatedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetsCreatedNotification>
{
	public ValueTask Handle(WidgetsCreatedNotification notification, CancellationToken cancellationToken)
	{
		return lifecycle.ChangedAsync(notification.Widgets);
	}
}

public sealed class CalendarWidgetUpdatedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetUpdatedNotification>
{
	public ValueTask Handle(WidgetUpdatedNotification notification, CancellationToken cancellationToken)
	{
		return lifecycle.ChangedAsync([notification.Widget]);
	}
}

public sealed class CalendarWidgetsUpdatedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetsUpdatedNotification>
{
	public ValueTask Handle(WidgetsUpdatedNotification notification, CancellationToken cancellationToken)
	{
		return lifecycle.ChangedAsync(notification.Widgets);
	}
}

public sealed class CalendarWidgetDeletedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetDeletedNotification>
{
	public ValueTask Handle(WidgetDeletedNotification notification, CancellationToken cancellationToken)
	{
		return lifecycle.DeletedAsync([notification.WidgetId]);
	}
}

public sealed class CalendarWidgetsDeletedHandler(CalendarWidgetLifecycle lifecycle)
	: INotificationHandler<WidgetsDeletedNotification>
{
	public ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
	{
		return lifecycle.DeletedAsync(notification.WidgetIds);
	}
}
