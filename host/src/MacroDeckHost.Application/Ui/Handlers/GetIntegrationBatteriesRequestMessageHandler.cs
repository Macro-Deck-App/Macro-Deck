using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Integrations;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetIntegrationBatteriesRequestMessageHandler
	: IUiTransportMessageHandler<GetIntegrationBatteriesRequest, GetIntegrationBatteriesResponse>
{
	private readonly IIntegrationRegistry _integrations;
	private readonly IBatteryRegistry _batteries;

	public GetIntegrationBatteriesRequestMessageHandler(IIntegrationRegistry integrations, IBatteryRegistry batteries)
	{
		_integrations = integrations;
		_batteries = batteries;
	}

	public ValueTask<GetIntegrationBatteriesResponse> Handle(
		GetIntegrationBatteriesRequest request,
		CancellationToken cancellationToken)
	{
		var integration = IntegrationCapabilityData.Find(_integrations, request.IntegrationId);
		if (integration is null)
		{
			return ValueTask.FromResult(new GetIntegrationBatteriesResponse());
		}

		return ValueTask.FromResult(new GetIntegrationBatteriesResponse
		{
			Found = true,
			Available = IntegrationCapabilityData.IsAvailable(_integrations, integration),
			Batteries =
			[
				.. _batteries.Devices
					.Where(device => device.ProviderId == integration.Id)
					.OrderBy(device => device.Name, StringComparer.CurrentCultureIgnoreCase)
					.Select(IntegrationBatteryDto.From)
			]
		});
	}
}
