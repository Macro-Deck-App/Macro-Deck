using MacroDeckHost.Application.Triggers;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetCreatedEventIndexHandler : INotificationHandler<WidgetCreatedNotification>
{
	private readonly IEventSubscriptionIndex _index;

	public WidgetCreatedEventIndexHandler(IEventSubscriptionIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetCreatedNotification notification, CancellationToken cancellationToken)
	{
		_index.ReindexWidget(notification.Widget.Id, notification.Widget.Data);
		return ValueTask.CompletedTask;
	}
}

public sealed class WidgetUpdatedEventIndexHandler : INotificationHandler<WidgetUpdatedNotification>
{
	private readonly IEventSubscriptionIndex _index;

	public WidgetUpdatedEventIndexHandler(IEventSubscriptionIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetUpdatedNotification notification, CancellationToken cancellationToken)
	{
		_index.ReindexWidget(notification.Widget.Id, notification.Widget.Data);
		return ValueTask.CompletedTask;
	}
}

public sealed class WidgetDeletedEventIndexHandler : INotificationHandler<WidgetDeletedNotification>
{
	private readonly IEventSubscriptionIndex _index;

	public WidgetDeletedEventIndexHandler(IEventSubscriptionIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetDeletedNotification notification, CancellationToken cancellationToken)
	{
		_index.Remove(EventTriggerOwner.ForWidget(notification.WidgetId));
		return ValueTask.CompletedTask;
	}
}

public sealed class WidgetsCreatedEventIndexHandler : INotificationHandler<WidgetsCreatedNotification>
{
	private readonly IEventSubscriptionIndex _index;

	public WidgetsCreatedEventIndexHandler(IEventSubscriptionIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetsCreatedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widget in notification.Widgets)
		{
			_index.ReindexWidget(widget.Id, widget.Data);
		}

		return ValueTask.CompletedTask;
	}
}

public sealed class WidgetsUpdatedEventIndexHandler : INotificationHandler<WidgetsUpdatedNotification>
{
	private readonly IEventSubscriptionIndex _index;

	public WidgetsUpdatedEventIndexHandler(IEventSubscriptionIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetsUpdatedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widget in notification.Widgets)
		{
			_index.ReindexWidget(widget.Id, widget.Data);
		}

		return ValueTask.CompletedTask;
	}
}

public sealed class WidgetsDeletedEventIndexHandler : INotificationHandler<WidgetsDeletedNotification>
{
	private readonly IEventSubscriptionIndex _index;

	public WidgetsDeletedEventIndexHandler(IEventSubscriptionIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widgetId in notification.WidgetIds)
		{
			_index.Remove(EventTriggerOwner.ForWidget(widgetId));
		}

		return ValueTask.CompletedTask;
	}
}

public sealed class ProfileDeletedEventIndexHandler : INotificationHandler<ProfileDeletedNotification>
{
	private readonly IEventSubscriptionIndex _index;

	public ProfileDeletedEventIndexHandler(IEventSubscriptionIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(ProfileDeletedNotification notification, CancellationToken cancellationToken)
	{
		_index.Rebuild();
		return ValueTask.CompletedTask;
	}
}

public sealed class AutomationCreatedEventIndexHandler : INotificationHandler<AutomationCreatedNotification>
{
	private readonly IEventSubscriptionIndex _index;

	public AutomationCreatedEventIndexHandler(IEventSubscriptionIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(AutomationCreatedNotification notification, CancellationToken cancellationToken)
	{
		var automation = notification.Automation;
		_index.ReindexAutomation(automation.Id, automation.Flows, automation.Enabled);
		return ValueTask.CompletedTask;
	}
}

public sealed class AutomationUpdatedEventIndexHandler : INotificationHandler<AutomationUpdatedNotification>
{
	private readonly IEventSubscriptionIndex _index;

	public AutomationUpdatedEventIndexHandler(IEventSubscriptionIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(AutomationUpdatedNotification notification, CancellationToken cancellationToken)
	{
		var automation = notification.Automation;

		_index.ReindexAutomation(automation.Id, automation.Flows, automation.Enabled);
		return ValueTask.CompletedTask;
	}
}

public sealed class AutomationDeletedEventIndexHandler : INotificationHandler<AutomationDeletedNotification>
{
	private readonly IEventSubscriptionIndex _index;

	public AutomationDeletedEventIndexHandler(IEventSubscriptionIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(AutomationDeletedNotification notification, CancellationToken cancellationToken)
	{
		_index.Remove(EventTriggerOwner.ForAutomation(notification.AutomationId));
		return ValueTask.CompletedTask;
	}
}
