namespace MacroDeck.Sdk.Weather;

/// <summary>The <see cref="IWeatherProviderApi" /> of a context that takes no weather notices: every call completes without effect.</summary>
public sealed class UnsupportedWeatherProviderApi : IWeatherProviderApi
{
	public static readonly UnsupportedWeatherProviderApi Instance = new();

	private UnsupportedWeatherProviderApi()
	{
	}

	public Task NotifySnapshotsChangedAsync(CancellationToken cancellationToken = default)
	{
		return Task.CompletedTask;
	}
}
