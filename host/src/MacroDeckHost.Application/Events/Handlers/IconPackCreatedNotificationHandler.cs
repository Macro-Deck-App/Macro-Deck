using MacroDeckHost.Application.Icons.Ownership;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IconPackCreatedNotificationHandler : INotificationHandler<IconPackCreatedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly IIconPackOwnerRegistry _ownerRegistry;

	public IconPackCreatedNotificationHandler(IUiTransport uiTransport, IIconPackOwnerRegistry ownerRegistry)
	{
		_uiTransport = uiTransport;
		_ownerRegistry = ownerRegistry;
	}

	public async ValueTask Handle(IconPackCreatedNotification notification, CancellationToken cancellationToken)
	{
		await _uiTransport.Send(new IconPackCreatedEvent
			{
				Pack = IconMapper.ToDto(notification.Pack,
					notification.IconCount,
					_ownerRegistry.Describe(notification.Pack))
			},
			cancellationToken);
	}
}
