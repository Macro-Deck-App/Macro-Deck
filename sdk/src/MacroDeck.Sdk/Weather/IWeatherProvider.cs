namespace MacroDeck.Sdk.Weather;

/// <summary>
/// Implemented by integrations that expose one or more weather stations (typically one per
/// configured location). The host enumerates instances across all enabled providers so the user
/// can pick which one a Weather widget shows.
/// </summary>
public interface IWeatherProvider
{
	/// <summary>Human-readable provider name, e.g. "Open-Meteo".</summary>
	/// <remarks>
	/// Optional. A provider that leaves this at the default is described by its integration's name
	/// instead - for a plugin, the <c>manifest.json</c> name - so the one place a name is stated stays
	/// the integration. Stating a name here still wins, which is what an integration exposing one or
	/// more distinctly-branded providers needs.
	/// </remarks>
	string ProviderName => string.Empty;

	/// <summary>The currently available stations (one per configured location).</summary>
	IReadOnlyList<WeatherStationInstance> GetInstances();

	/// <summary>Resolves a station by its provider-local instance id, or <c>null</c> if unknown.</summary>
	IWeatherStation? GetStation(string instanceId);
}
