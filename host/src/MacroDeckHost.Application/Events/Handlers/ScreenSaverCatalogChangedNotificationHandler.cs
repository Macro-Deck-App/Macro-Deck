using MacroDeckHost.Application.ScreenSavers;
using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.ScreenSavers;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class ScreenSaverCatalogChangedNotificationHandler
	: INotificationHandler<ScreenSaverCatalogChangedNotification>
{
	private readonly IScreenSaverRegistry _registry;
	private readonly IUiTransport _uiTransport;

	public ScreenSaverCatalogChangedNotificationHandler(IScreenSaverRegistry registry, IUiTransport uiTransport)
	{
		_registry = registry;
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(
		ScreenSaverCatalogChangedNotification notification,
		CancellationToken cancellationToken)
	{
		var evt = new ScreenSaverCatalogChangedEvent
		{
			ScreenSavers = ScreenSaverDtoMapper.MapToDto(_registry.GetAll())
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
