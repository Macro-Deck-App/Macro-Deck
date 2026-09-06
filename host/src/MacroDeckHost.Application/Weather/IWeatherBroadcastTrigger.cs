namespace MacroDeckHost.Application.Weather;

public interface IWeatherBroadcastTrigger
{
	void RequestRefresh();

	Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
