using MacroDeckHost.Application.Profiles;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class VirtualProfilesChangedHandler
	: INotificationHandler<VirtualProfilesChangedNotification>,
		INotificationHandler<IntegrationStateChangedNotification>,
		INotificationHandler<IntegrationCatalogChangedNotification>
{
	private readonly VirtualProfileAnnouncer _announcer;

	public VirtualProfilesChangedHandler(VirtualProfileAnnouncer announcer)
	{
		_announcer = announcer;
	}

	public ValueTask Handle(VirtualProfilesChangedNotification notification, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(notification);
		return new ValueTask(_announcer.AnnounceAsync(notification.IntegrationId, cancellationToken));
	}

	public ValueTask Handle(IntegrationStateChangedNotification notification, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(notification);
		return new ValueTask(_announcer.AnnounceAsync(notification.IntegrationId, cancellationToken));
	}

	public ValueTask Handle(IntegrationCatalogChangedNotification notification, CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(notification);
		return new ValueTask(_announcer.AnnounceAsync(notification.IntegrationId, cancellationToken));
	}
}
