using MacroDeckHost.Application.Rendering;
using MacroDeckHost.Application.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

/// <summary>
/// Evicts <see cref="IWidgetIconResources" />'s cache entry for an icon the moment it changed or was
/// deleted, so a re-imported icon does not keep drawing its old bytes on every deck for the rest of the
/// process lifetime - the bounded-LRU follow-up <c>WidgetIconResources</c>'s own doc comment named. Also
/// raises <see cref="IWidgetRenderSignals.RaiseIconInvalidated" /> beside the evict, which is what reaches
/// an already-open session: the evict alone only stops a *future* resolve from handing out the stale
/// handle, it does nothing for a session that resolved (and is still serving) it before the evict ran.
/// </summary>
public sealed class WidgetIconResourceInvalidationHandler
	: INotificationHandler<IconUpdatedNotification>, INotificationHandler<IconDeletedNotification>
{
	private readonly IWidgetIconResources _iconResources;
	private readonly IWidgetRenderSignals _renderSignals;

	public WidgetIconResourceInvalidationHandler(IWidgetIconResources iconResources, IWidgetRenderSignals renderSignals)
	{
		_iconResources = iconResources;
		_renderSignals = renderSignals;
	}

	public ValueTask Handle(IconUpdatedNotification notification, CancellationToken cancellationToken)
	{
		_iconResources.Evict(notification.Icon.Id);
		_renderSignals.RaiseIconInvalidated(notification.Icon.Id);

		return ValueTask.CompletedTask;
	}

	public ValueTask Handle(IconDeletedNotification notification, CancellationToken cancellationToken)
	{
		_iconResources.Evict(notification.IconId);
		_renderSignals.RaiseIconInvalidated(notification.IconId);

		return ValueTask.CompletedTask;
	}
}
