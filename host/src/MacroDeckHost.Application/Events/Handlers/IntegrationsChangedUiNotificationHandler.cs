using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using Mediator;

namespace MacroDeckHost.Application.Events.Handlers;

public sealed class IntegrationsChangedUiNotificationHandler
	: INotificationHandler<IntegrationStateChangedNotification>,
		INotificationHandler<IntegrationCatalogChangedNotification>
{
	private readonly IUiTransport _uiTransport;

	public IntegrationsChangedUiNotificationHandler(IUiTransport uiTransport)
	{
		_uiTransport = uiTransport;
	}

	public ValueTask Handle(IntegrationStateChangedNotification notification, CancellationToken cancellationToken)
		=> Send(notification.IntegrationId, cancellationToken);

	public ValueTask Handle(IntegrationCatalogChangedNotification notification, CancellationToken cancellationToken)
		=> Send(notification.IntegrationId, cancellationToken);

	private ValueTask Send(string integrationId, CancellationToken cancellationToken)
		=> new(_uiTransport.SendToGroup(UiAdminGroups.Admin,
			new IntegrationsChangedEvent { IntegrationId = integrationId },
			cancellationToken));
}
