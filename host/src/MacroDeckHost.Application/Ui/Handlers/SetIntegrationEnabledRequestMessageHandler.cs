using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;
using Mediator;
using MacroDeckHost.Localization;

namespace MacroDeckHost.Application.Ui.Handlers;

public class SetIntegrationEnabledRequestMessageHandler
	: IUiTransportMessageHandler<SetIntegrationEnabledRequest, SetIntegrationEnabledResponse>
{
	private readonly IIntegrationRegistry _integrationRegistry;
	private readonly IIntegrationLifecycle _lifecycle;
	private readonly IMediator _mediator;

	public SetIntegrationEnabledRequestMessageHandler(
		IIntegrationRegistry integrationRegistry,
		IIntegrationLifecycle lifecycle,
		IMediator mediator)
	{
		_integrationRegistry = integrationRegistry;
		_lifecycle = lifecycle;
		_mediator = mediator;
	}

	public async ValueTask<SetIntegrationEnabledResponse> Handle(
		SetIntegrationEnabledRequest request,
		CancellationToken cancellationToken)
	{
		var integration = _integrationRegistry.Integrations
			.FirstOrDefault(i => i.Id == request.Id);

		if (integration is null)
		{
			return new SetIntegrationEnabledResponse
			{
				Success = false,
				Error = new TransportError
				{
					Code = "NOT_FOUND",
					Message = AppStrings.Errors.Integrations.NotFound(id: request.Id)
				}
			};
		}

		_integrationRegistry.SetEnabled(request.Id, request.Enabled);

		if (request.Enabled)
		{
			await _lifecycle.ReinitializeAsync(request.Id, cancellationToken);
		}
		else
		{
			await integration.ShutdownAsync();
			await _mediator.Publish(new IntegrationStateChangedNotification(request.Id), cancellationToken);
		}

		return new SetIntegrationEnabledResponse { Success = true };
	}
}
