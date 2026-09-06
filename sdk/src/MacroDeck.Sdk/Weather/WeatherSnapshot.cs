namespace MacroDeck.Sdk.Weather;

/// <summary>
/// Provider-agnostic snapshot of a location's current conditions plus a multi-day forecast.
/// Returned by <see cref="IWeatherStation.GetSnapshotAsync"/> and pushed to the Weather widget.
/// Temperatures are already expressed in <see cref="Unit"/>.
/// </summary>
public sealed record WeatherSnapshot
{
	/// <summary>False when the provider could not obtain data (network error, not yet loaded, …).</summary>
	public bool IsAvailable { get; init; }

	public string LocationName { get; init; } = string.Empty;

	public double? Temperature { get; init; }

	public double? ApparentTemperature { get; init; }

	public WeatherCondition Condition { get; init; }

	/// <summary>True for daytime, false for night - lets the UI pick a day/night icon variant.</summary>
	public bool IsDay { get; init; }

	public TemperatureUnit Unit { get; init; }

	public IReadOnlyList<WeatherForecastDay> Days { get; init; } = [];

	/// <summary>
	/// Hour-by-hour forecast from the current hour onward, or empty when the provider reports none. A
	/// consumer must handle empty rather than assume a station supplies it - this was added after the
	/// contract shipped, so a station written against the earlier SDK never fills it.
	/// </summary>
	public IReadOnlyList<WeatherHour> Hours { get; init; } = [];

	/// <summary>
	/// Wind speed, in km/h when <see cref="Unit" /> is Celsius and mph when it is Fahrenheit - the
	/// measurement system the chosen temperature unit implies. Null when the provider reports none.
	/// </summary>
	public double? WindSpeed { get; init; }

	/// <summary>Direction the wind blows <i>from</i>, in meteorological degrees (0 = north, 90 = east).
	/// Null when the provider reports none.</summary>
	public double? WindDirection { get; init; }

	/// <summary>Relative humidity in <c>0..100</c>. Null when the provider reports none.</summary>
	public double? Humidity { get; init; }

	/// <summary>
	/// Precipitation in the current hour, in millimetres when <see cref="Unit" /> is Celsius and inches
	/// when it is Fahrenheit - see <see cref="WindSpeed" />. Null when the provider reports none.
	/// </summary>
	public double? Precipitation { get; init; }

	/// <summary>Today's sunrise, with the location's own offset. Null when the provider reports
	/// none.</summary>
	public DateTimeOffset? Sunrise { get; init; }

	/// <summary>Today's sunset, with the location's own offset. Null when the provider reports
	/// none.</summary>
	public DateTimeOffset? Sunset { get; init; }

	/// <summary>An unavailable snapshot carrying only the location name, if known.</summary>
	public static WeatherSnapshot Unavailable(string locationName = "") => new()
	{
		IsAvailable = false,
		LocationName = locationName
	};
}
