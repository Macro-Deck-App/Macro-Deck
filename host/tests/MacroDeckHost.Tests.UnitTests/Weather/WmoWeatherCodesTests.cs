using MacroDeckHost.Integrations.Weather;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
public class WmoWeatherCodesTests
{
	[TestCase(0, WeatherCondition.Clear)]
	[TestCase(1, WeatherCondition.MainlyClear)]
	[TestCase(2, WeatherCondition.PartlyCloudy)]
	[TestCase(3, WeatherCondition.Overcast)]
	[TestCase(45, WeatherCondition.Fog)]
	[TestCase(48, WeatherCondition.Fog)]
	[TestCase(53, WeatherCondition.Drizzle)]
	[TestCase(56, WeatherCondition.FreezingRain)]
	[TestCase(63, WeatherCondition.Rain)]
	[TestCase(66, WeatherCondition.FreezingRain)]
	[TestCase(73, WeatherCondition.Snow)]
	[TestCase(77, WeatherCondition.SnowGrains)]
	[TestCase(81, WeatherCondition.RainShowers)]
	[TestCase(86, WeatherCondition.SnowShowers)]
	[TestCase(95, WeatherCondition.Thunderstorm)]
	[TestCase(99, WeatherCondition.Thunderstorm)]
	[TestCase(1234, WeatherCondition.Unknown)]
	public void Map_ReturnsExpectedCondition(int code, WeatherCondition expected)
	{
		Assert.That(WmoWeatherCodes.Map(code), Is.EqualTo(expected));
	}
}
