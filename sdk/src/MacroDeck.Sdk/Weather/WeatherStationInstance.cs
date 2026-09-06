namespace MacroDeck.Sdk.Weather;

/// <summary>
/// A selectable weather station exposed by a provider - typically one per configured location.
/// <see cref="Id"/> is unique within the provider; <see cref="DisplayName"/> is shown to the user
/// (e.g. "Berlin, Germany").
/// </summary>
public sealed record WeatherStationInstance(string Id, string DisplayName);
