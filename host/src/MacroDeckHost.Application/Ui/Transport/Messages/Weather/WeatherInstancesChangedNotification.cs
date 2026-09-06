namespace MacroDeckHost.Application.Ui.Transport.Messages.Weather;

public class WeatherInstancesChangedNotification
{
	public List<WeatherInstanceDto> Instances { get; set; } = new();
}
