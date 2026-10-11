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

	/// <summary>
	/// The action a press on a Weather widget runs when the widget has no flow of its own, or <c>null</c>
	/// (the default) for no such press.
	/// </summary>
	/// <remarks>
	/// Only a client with a screen runs it, never a hardware deck. The host resolves the station the widget
	/// shows, which is its configured station or, without one, the first station of all providers, and runs
	/// this action of the station's own provider with that station in
	/// <see cref="WeatherDetailsAction.StationParameter" />.
	/// A host that predates this member never runs it.
	/// </remarks>
	WeatherDetailsAction? DetailsAction => null;
}
