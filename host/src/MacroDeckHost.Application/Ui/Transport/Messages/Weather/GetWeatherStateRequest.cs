namespace MacroDeckHost.Application.Ui.Transport.Messages.Weather;

public class GetWeatherStateRequest
{
	public string? InstanceId { get; set; }
}

public class GetWeatherStateResponse
{
	public WeatherStatePayload State { get; set; } = new();
}
