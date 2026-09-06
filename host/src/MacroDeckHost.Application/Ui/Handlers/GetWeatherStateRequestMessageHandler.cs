using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Application.Weather;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Application.Ui.Handlers;

public class GetWeatherStateRequestMessageHandler
	: IUiTransportMessageHandler<GetWeatherStateRequest, GetWeatherStateResponse>
{
	private readonly IWeatherRegistry _registry;
	private readonly ILogger _logger;

	public GetWeatherStateRequestMessageHandler(IWeatherRegistry registry, ILogger logger)
	{
		_registry = registry;
		_logger = logger.ForContext<GetWeatherStateRequestMessageHandler>();
	}

	public async ValueTask<GetWeatherStateResponse> Handle(
		GetWeatherStateRequest request,
		CancellationToken cancellationToken)
	{
		var instances = _registry.GetInstances();
		var instanceId = request.InstanceId ?? (instances.Count > 0 ? instances[0].InstanceId : null);

		var station = instanceId is null ? null : _registry.GetStation(instanceId);
		if (station is null)
		{
			// A widget can outlive its location (removed here, or imported from another machine), and the
			// client cannot tell that from "no snapshot yet" - only the registry can, so say so (issue #132).
			return new GetWeatherStateResponse { State = WeatherStatePayload.UnknownStation(instanceId) };
		}

		try
		{
			var snapshot = await station.GetSnapshotAsync(cancellationToken);
			return new GetWeatherStateResponse { State = WeatherStatePayload.From(snapshot, instanceId) };
		}
		catch (Exception ex)
		{
			_logger.Error(ex, "Failed to read weather state");
			return new GetWeatherStateResponse { State = WeatherStatePayload.Unavailable(instanceId) };
		}
	}
}
