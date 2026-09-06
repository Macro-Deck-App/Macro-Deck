namespace MacroDeck.Sdk.Weather;

/// <summary>
/// One day of forecast: the expected condition and the low/high temperatures, expressed in the
/// snapshot's <see cref="WeatherSnapshot.Unit"/>.
/// </summary>
public sealed record WeatherForecastDay(DateOnly Date, WeatherCondition Condition, double Min, double Max);
