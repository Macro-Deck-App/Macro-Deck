using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Widgets;
using MacroDeckHost.Application.Widgets;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class WidgetTypeCatalogChangedNotificationHandler
	: INotificationHandler<WidgetTypeCatalogChangedNotification>
{
	private readonly IWidgetTypeRegistry _registry;
	private readonly IUiTransport _uiTransport;

	public WidgetTypeCatalogChangedNotificationHandler(IWidgetTypeRegistry registry, IUiTransport uiTransport)
	{
		_registry = registry;
		_uiTransport = uiTransport;
	}

	public async ValueTask Handle(
		WidgetTypeCatalogChangedNotification notification,
		CancellationToken cancellationToken)
	{
		var evt = new WidgetTypeCatalogChangedEvent
		{
			Types = WidgetTypeDtoMapper.MapToDto(_registry.All)
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
