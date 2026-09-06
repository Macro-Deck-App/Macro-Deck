using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Application.Weather;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetWeatherInstancesRequestMessageHandler
	: IUiTransportMessageHandler<GetWeatherInstancesRequest, GetWeatherInstancesResponse>
{
	private readonly IWeatherRegistry _registry;

	public GetWeatherInstancesRequestMessageHandler(IWeatherRegistry registry)
	{
		_registry = registry;
	}

	public ValueTask<GetWeatherInstancesResponse> Handle(
		GetWeatherInstancesRequest request,
		CancellationToken cancellationToken)
	{
		var instances = _registry.GetInstances()
			.Select(WeatherInstanceDto.From)
			.ToList();

		return ValueTask.FromResult(new GetWeatherInstancesResponse { Instances = instances });
	}
}
