using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IconUpdatedNotificationHandler : INotificationHandler<IconUpdatedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly IIconPackCache _iconPackCache;

	public IconUpdatedNotificationHandler(IUiTransport uiTransport, IIconPackCache iconPackCache)
	{
		_uiTransport = uiTransport;
		_iconPackCache = iconPackCache;
	}

	public async ValueTask Handle(IconUpdatedNotification notification, CancellationToken cancellationToken)
	{
		if (notification.Icon.AppearanceOfId is not null)
		{
			return;
		}

		await _uiTransport.Send(new IconUpdatedEvent
			{
				Icon = IconMapper.ToDto(notification.Icon, _iconPackCache)
			},
			cancellationToken);
	}
}
