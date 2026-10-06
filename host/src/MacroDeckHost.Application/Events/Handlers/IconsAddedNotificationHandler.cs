using MacroDeckHost.Application.Caching;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Icons;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IconsAddedNotificationHandler : INotificationHandler<IconsAddedNotification>
{
	private readonly IUiTransport _uiTransport;
	private readonly IIconPackCache _iconPackCache;

	public IconsAddedNotificationHandler(IUiTransport uiTransport, IIconPackCache iconPackCache)
	{
		_uiTransport = uiTransport;
		_iconPackCache = iconPackCache;
	}

	public async ValueTask Handle(IconsAddedNotification notification, CancellationToken cancellationToken)
	{
		var icons = notification.Icons.Where(icon => icon.AppearanceOfId is null).ToList();
		if (icons.Count == 0 && notification.Icons.Count > 0)
		{
			return;
		}

		await _uiTransport.Send(new IconsAddedEvent
			{
				BatchId = notification.BatchId?.ToString(),
				PackId = notification.PackId.ToString(),
				Icons = icons.Select(icon => IconMapper.ToDto(icon, _iconPackCache)).ToList()
			},
			cancellationToken);
	}
}
