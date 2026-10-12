using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Sdk.Weather;

namespace MacroDeck.Plugin.Hosting.Integrations.HostApis;

internal sealed class RemoteWeatherProviderApi(IHostInvoker invoker) : IWeatherProviderApi
{
	private readonly Lock _gate = new();
	private Task? _inFlight;
	private Task? _queued;

	public Task NotifySnapshotsChangedAsync(CancellationToken cancellationToken = default)
	{
		Task pending;
		lock (_gate)
		{
			if (_queued is not null)
			{
				pending = _queued;
			}
			else if (_inFlight is not null)
			{
				// The notice on its way may be read before the change behind this one, so one follow-up is
				// queued behind it and every later caller shares that follow-up.
				pending = _queued = SendAfterAsync(_inFlight);
			}
			else
			{
				pending = _inFlight = SendAsync();
			}
		}

		return pending.WaitAsync(cancellationToken);
	}

	private async Task SendAfterAsync(Task inFlight)
	{
		await Task.Yield();
		await inFlight.ConfigureAwait(false);
		lock (_gate)
		{
			_inFlight = _queued;
			_queued = null;
		}

		await InvokeAsync().ConfigureAwait(false);
		lock (_gate)
		{
			_inFlight = null;
		}
	}

	private async Task SendAsync()
	{
		await Task.Yield();
		await InvokeAsync().ConfigureAwait(false);
		lock (_gate)
		{
			if (_queued is null)
			{
				_inFlight = null;
			}
		}
	}

	private async Task InvokeAsync()
	{
		try
		{
			await invoker.InvokeAsync(Protocol.Callbacks.HostApis.Weather,
					HostOperations.Weather.SnapshotsChanged,
					null,
					CancellationToken.None)
				.ConfigureAwait(false);
		}
		catch (Exception exception) when (exception is not OutOfMemoryException)
		{
			// Best effort by contract: an older host answers CAPABILITY_UNSUPPORTED, a plugin without the weather
			// capability CAPABILITY_UNAVAILABLE, and either way the host still reads the stations on its interval.
		}
	}
}
