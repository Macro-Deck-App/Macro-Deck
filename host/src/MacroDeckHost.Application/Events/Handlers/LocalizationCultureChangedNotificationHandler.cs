using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Localization;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class LocalizationCultureChangedNotificationHandler
	: INotificationHandler<LocalizationCultureChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public LocalizationCultureChangedNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(LocalizationCultureChangedNotification notification, CancellationToken cancellationToken)
	{
		var evt = new LocalizationCultureChangedEvent
		{
			Culture = notification.Culture,
			FallbackCulture = notification.FallbackCulture
		};
		return new ValueTask(_uiTransport.Send(evt, cancellationToken));
	}
}
