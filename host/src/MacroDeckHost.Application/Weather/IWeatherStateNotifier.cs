using MacroDeckHost.Application.Ui.Transport.Messages.Weather;

namespace MacroDeckHost.Application.Weather;

public sealed class WeatherStateChangedEventArgs : EventArgs
{
	public WeatherStateChangedEventArgs(string instanceId, WeatherStatePayload state)
	{
		InstanceId = instanceId;
		State = state;
	}

	public string InstanceId { get; }

	public WeatherStatePayload State { get; }
}

public interface IWeatherStateNotifier
{
	event EventHandler<WeatherStateChangedEventArgs>? StateChanged;

	void Publish(string instanceId, WeatherStatePayload state);
}

public sealed class WeatherStateNotifier : IWeatherStateNotifier
{
	public event EventHandler<WeatherStateChangedEventArgs>? StateChanged;

	public void Publish(string instanceId, WeatherStatePayload state)
		=> StateChanged?.Invoke(this, new WeatherStateChangedEventArgs(instanceId, state));
}
