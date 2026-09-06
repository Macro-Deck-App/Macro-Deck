namespace MacroDeckHost.Application.Ui.Transport.Messages.Weather;

public class WeatherStateChangedNotification
{
	public WeatherStatePayload State { get; set; } = new();
}
