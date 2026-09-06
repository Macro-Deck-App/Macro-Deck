namespace MacroDeck.Sdk.Weather;

/// <summary>
/// A compact, provider-agnostic weather condition. Providers map their own condition codes
/// (e.g. WMO weather codes) onto this set so the UI can pick a matching icon.
/// </summary>
public enum WeatherCondition
{
	Unknown = 0,
	Clear = 1,
	MainlyClear = 2,
	PartlyCloudy = 3,
	Overcast = 4,
	Fog = 5,
	Drizzle = 6,
	Rain = 7,
	FreezingRain = 8,
	Snow = 9,
	SnowGrains = 10,
	RainShowers = 11,
	SnowShowers = 12,
	Thunderstorm = 13
}
