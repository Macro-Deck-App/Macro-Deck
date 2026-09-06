using MacroDeckHost.Application.Variables;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetCreatedVariableIndexHandler : INotificationHandler<WidgetCreatedNotification>
{
	private readonly IWidgetVariableIndex _index;

	public WidgetCreatedVariableIndexHandler(IWidgetVariableIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetCreatedNotification notification, CancellationToken cancellationToken)
	{
		_index.ReindexWidget(notification.Widget.Id, notification.Widget.Type, notification.Widget.Data);
		return ValueTask.CompletedTask;
	}
}

public sealed class WidgetUpdatedVariableIndexHandler : INotificationHandler<WidgetUpdatedNotification>
{
	private readonly IWidgetVariableIndex _index;

	public WidgetUpdatedVariableIndexHandler(IWidgetVariableIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetUpdatedNotification notification, CancellationToken cancellationToken)
	{
		_index.ReindexWidget(notification.Widget.Id, notification.Widget.Type, notification.Widget.Data);
		return ValueTask.CompletedTask;
	}
}

public sealed class WidgetDeletedVariableIndexHandler : INotificationHandler<WidgetDeletedNotification>
{
	private readonly IWidgetVariableIndex _index;

	public WidgetDeletedVariableIndexHandler(IWidgetVariableIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetDeletedNotification notification, CancellationToken cancellationToken)
	{
		_index.Remove(notification.WidgetId);
		return ValueTask.CompletedTask;
	}
}

public sealed class WidgetsCreatedVariableIndexHandler : INotificationHandler<WidgetsCreatedNotification>
{
	private readonly IWidgetVariableIndex _index;

	public WidgetsCreatedVariableIndexHandler(IWidgetVariableIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetsCreatedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widget in notification.Widgets)
		{
			_index.ReindexWidget(widget.Id, widget.Type, widget.Data);
		}

		return ValueTask.CompletedTask;
	}
}

public sealed class WidgetsUpdatedVariableIndexHandler : INotificationHandler<WidgetsUpdatedNotification>
{
	private readonly IWidgetVariableIndex _index;

	public WidgetsUpdatedVariableIndexHandler(IWidgetVariableIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetsUpdatedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widget in notification.Widgets)
		{
			_index.ReindexWidget(widget.Id, widget.Type, widget.Data);
		}

		return ValueTask.CompletedTask;
	}
}

public sealed class WidgetsDeletedVariableIndexHandler : INotificationHandler<WidgetsDeletedNotification>
{
	private readonly IWidgetVariableIndex _index;

	public WidgetsDeletedVariableIndexHandler(IWidgetVariableIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(WidgetsDeletedNotification notification, CancellationToken cancellationToken)
	{
		foreach (var widgetId in notification.WidgetIds)
		{
			_index.Remove(widgetId);
		}

		return ValueTask.CompletedTask;
	}
}

public sealed class ProfileDeletedVariableIndexHandler : INotificationHandler<ProfileDeletedNotification>
{
	private readonly IWidgetVariableIndex _index;

	public ProfileDeletedVariableIndexHandler(IWidgetVariableIndex index)
	{
		_index = index;
	}

	public ValueTask Handle(ProfileDeletedNotification notification, CancellationToken cancellationToken)
	{
		_index.Rebuild();
		return ValueTask.CompletedTask;
	}
}
