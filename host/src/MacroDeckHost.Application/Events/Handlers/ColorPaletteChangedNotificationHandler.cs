using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ColorPaletteChangedNotificationHandler : INotificationHandler<ColorPaletteChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public ColorPaletteChangedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(ColorPaletteChangedNotification notification, CancellationToken cancellationToken)
		=> await _uiTransport.SendToGroup(UiAdminGroups.Admin,
			new ColorPaletteChangedEvent { Colors = notification.Colors },
			cancellationToken);
}
