using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Settings;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class AppearanceChangedNotificationHandler : INotificationHandler<AppearanceChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public AppearanceChangedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(AppearanceChangedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new AppearanceChangedEvent
		{
			ThemeMode = notification.ThemeMode,
			AccentColor = notification.AccentColor,
			FontFamily = notification.FontFamily
		};
		return new ValueTask(_uiTransport.Send(evt, cancellationToken));
	}
}
