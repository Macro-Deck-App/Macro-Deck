using MacroDeck.Sdk.Weather;
using MacroDeckHost.Application.Weather;

namespace MacroDeckHost.Infrastructure.Integrations;

internal sealed class IntegrationWeatherProviderApi : IWeatherProviderApi
{
	private readonly IWeatherBroadcastTrigger _trigger;

	public IntegrationWeatherProviderApi(IWeatherBroadcastTrigger trigger)
	{
		_trigger = trigger;
	}

	public Task NotifySnapshotsChangedAsync(CancellationToken cancellationToken = default)
	{
		if (cancellationToken.IsCancellationRequested)
		{
			return Task.FromCanceled(cancellationToken);
		}

		try
		{
			_trigger.RequestRefresh();
		}
		catch (ObjectDisposedException)
		{
			// The trigger goes away with the host; a notice racing shutdown has nothing left to wake.
		}

		return Task.CompletedTask;
	}
}
