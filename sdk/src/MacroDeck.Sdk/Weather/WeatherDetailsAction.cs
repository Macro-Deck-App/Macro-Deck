namespace MacroDeck.Sdk.Weather;

/// <summary>
/// An action of the weather provider's own integration that shows the details of one station. Pressing a
/// Weather widget without its own flow runs it on the pressing client, for the station the widget shows.
/// </summary>
/// <param name="ActionId">The id of one of the integration's own <c>Actions</c>.</param>
/// <param name="StationParameter">The action parameter that receives the station, as the qualified station
/// id the widget stores (<c>integrationId::localId</c>).</param>
public sealed record WeatherDetailsAction(string ActionId, string StationParameter);
