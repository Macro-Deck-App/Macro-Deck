namespace MacroDeck.Sdk.Weather;

/// <summary>
/// One hour of forecast. <see cref="Temperature" /> is expressed in the snapshot's
/// <see cref="WeatherSnapshot.Unit" />, like every other temperature it carries.
/// </summary>
/// <param name="Time">The hour this entry describes, with the location's own offset.</param>
/// <param name="Condition">The expected condition.</param>
/// <param name="Temperature">The expected temperature.</param>
/// <param name="PrecipitationProbability">Chance of precipitation in <c>0..100</c>, or null when the
/// provider does not report one.</param>
public sealed record WeatherHour(
	DateTimeOffset Time,
	WeatherCondition Condition,
	double Temperature,
	double? PrecipitationProbability = null);
