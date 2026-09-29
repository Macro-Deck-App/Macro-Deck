using MacroDeckHost.Application.Timers;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class TimerWidgetCreatedHandler : INotificationHandler<WidgetCreatedNotification>
{
	private readonly TimerWidgetCoordinator _timers;

	public TimerWidgetCreatedHandler(TimerWidgetCoordinator timers)
	{
		_timers = timers;
	}

	public async ValueTask Handle(WidgetCreatedNotification notification, CancellationToken cancellationToken)
	{
		if (TimerWidgetConfig.IsTimerType(notification.Widget.Type))
		{
			await _timers.SyncAsync(notification.Widget).ConfigureAwait(false);
		}
	}
}

public sealed class TimerWidgetsCreatedHandler : INotificationHandler<WidgetsCreatedNotification>
{
	private readonly TimerWidgetCoordinator _timers;

	public TimerWidgetsCreatedHandler(TimerWidgetCoordinator timers)
	{
		_timers = timers;
	}

	public async ValueTask Handle(WidgetsCreatedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widget in notification.Widgets.Where(widget => TimerWidgetConfig.IsTimerType(widget.Type)))
		{
			await _timers.SyncAsync(widget).ConfigureAwait(false);
		}
	}
}

public sealed class TimerWidgetUpdatedHandler : INotificationHandler<WidgetUpdatedNotification>
{
	private readonly TimerWidgetCoordinator _timers;

	public TimerWidgetUpdatedHandler(TimerWidgetCoordinator timers)
	{
		_timers = timers;
	}

	public async ValueTask Handle(WidgetUpdatedNotification notification, CancellationToken cancellationToken)
	{
		if (TimerWidgetConfig.IsTimerType(notification.Widget.Type) || _timers.Store.Contains(notification.Widget.Id))
		{
			await _timers.SyncAsync(notification.Widget).ConfigureAwait(false);
		}
	}
}

public sealed class TimerWidgetsUpdatedHandler : INotificationHandler<WidgetsUpdatedNotification>
{
	private readonly TimerWidgetCoordinator _timers;

	public TimerWidgetsUpdatedHandler(TimerWidgetCoordinator timers)
	{
		_timers = timers;
	}

	public async ValueTask Handle(WidgetsUpdatedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widget in notification.Widgets)
		{
			if (TimerWidgetConfig.IsTimerType(widget.Type) || _timers.Store.Contains(widget.Id))
			{
				await _timers.SyncAsync(widget).ConfigureAwait(false);
			}
		}
	}
}

public sealed class TimerWidgetDeletedHandler : INotificationHandler<WidgetDeletedNotification>
{
	private readonly TimerWidgetCoordinator _timers;

	public TimerWidgetDeletedHandler(TimerWidgetCoordinator timers)
	{
		_timers = timers;
	}

	public async ValueTask Handle(WidgetDeletedNotification notification, CancellationToken cancellationToken)
		=> await _timers.ForgetAsync(notification.WidgetId).ConfigureAwait(false);
}

public sealed class TimerWidgetsDeletedHandler : INotificationHandler<WidgetsDeletedNotification>
{
	private readonly TimerWidgetCoordinator _timers;

	public TimerWidgetsDeletedHandler(TimerWidgetCoordinator timers)
	{
		_timers = timers;
	}

	public async ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widgetId in notification.WidgetIds)
		{
			await _timers.ForgetAsync(widgetId).ConfigureAwait(false);
		}
	}
}

// A deleted profile takes its folders out of the cache without a deletion notice per widget.
public sealed class TimerWidgetProfileDeletedHandler : INotificationHandler<ProfileDeletedNotification>
{
	private readonly TimerWidgetCoordinator _timers;

	public TimerWidgetProfileDeletedHandler(TimerWidgetCoordinator timers)
	{
		_timers = timers;
	}

	public async ValueTask Handle(ProfileDeletedNotification notification, CancellationToken cancellationToken)
		=> await _timers.PruneMissingAsync().ConfigureAwait(false);
}
