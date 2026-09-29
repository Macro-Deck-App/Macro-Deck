using MacroDeckHost.Application.Integrations;
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
	private readonly IIntegrationRegistry _integrations;

	public WidgetTypeCatalogChangedNotificationHandler(IWidgetTypeRegistry registry,
		IUiTransport uiTransport,
		IIntegrationRegistry integrations)
	{
		_registry = registry;
		_uiTransport = uiTransport;
		_integrations = integrations;
	}

	public async ValueTask Handle(
		WidgetTypeCatalogChangedNotification notification,
		CancellationToken cancellationToken)
	{
		var evt = new WidgetTypeCatalogChangedEvent
		{
			Types = WidgetTypeDtoMapper.MapToDto(_registry.All, _integrations)
		};

		await _uiTransport.Send(evt, cancellationToken);
	}
}
