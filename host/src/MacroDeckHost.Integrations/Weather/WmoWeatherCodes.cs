using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Integrations.Weather;

internal static class WmoWeatherCodes
{
	public static WeatherCondition Map(int code) => code switch
	{
		0 => WeatherCondition.Clear,
		1 => WeatherCondition.MainlyClear,
		2 => WeatherCondition.PartlyCloudy,
		3 => WeatherCondition.Overcast,
		45 or 48 => WeatherCondition.Fog,
		51 or 53 or 55 => WeatherCondition.Drizzle,
		56 or 57 => WeatherCondition.FreezingRain,
		61 or 63 or 65 => WeatherCondition.Rain,
		66 or 67 => WeatherCondition.FreezingRain,
		71 or 73 or 75 => WeatherCondition.Snow,
		77 => WeatherCondition.SnowGrains,
		80 or 81 or 82 => WeatherCondition.RainShowers,
		85 or 86 => WeatherCondition.SnowShowers,
		95 or 96 or 99 => WeatherCondition.Thunderstorm,
		_ => WeatherCondition.Unknown
	};
}
