namespace MacroDeck.Sdk.Weather;

/// <summary>
/// A single configured weather location. The host reads its latest snapshot to push weather state
/// to the UI and to feed the weather variables. Implementations should return a cached snapshot and
/// refresh it on their own slow interval rather than fetching on every call.
/// </summary>
public interface IWeatherStation
{
	/// <summary>The most recent weather snapshot for this location.</summary>
	Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct);
}
