namespace MacroDeck.Sdk.Weather;

/// <summary>
/// What a weather provider tells Macro Deck about its stations. Reached through
/// <see cref="IIntegrationContext.WeatherProvider" />.
/// </summary>
public interface IWeatherProviderApi
{
	/// <summary>
	/// Tells Macro Deck that what <see cref="IWeatherStation.GetSnapshotAsync" /> returns has changed, so it
	/// reads every station again now and Weather widgets show the new conditions instead of waiting for its
	/// next periodic read. Call it after a station fetched new data. Requests made while one is pending are
	/// merged into one read.
	/// </summary>
	/// <remarks>
	/// Best effort: it never throws because the notice could not be delivered. A Macro Deck that predates it
	/// still reads the stations about once a minute. It does not announce a changed list of stations: a plugin
	/// reports that with a <c>weather</c> catalog change, an integration built into Macro Deck by being
	/// initialized again. Out of process it requires the negotiated <c>weather</c> capability.
	/// </remarks>
	Task NotifySnapshotsChangedAsync(CancellationToken cancellationToken = default);
}
