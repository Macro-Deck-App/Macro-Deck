using MacroDeckHost.Integrations.Weather;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
internal sealed class WeatherStationTests
{
	[Test]
	public async Task RefreshAsync_retains_last_snapshot_when_the_current_block_is_missing()
	{
		var client = new FakeOpenMeteoClient { Forecast = ForecastWith(21) };
		var station = new WeatherStation(client, 52.5, 13.4, TemperatureUnit.Celsius, "Berlin");

		await station.RefreshAsync(CancellationToken.None);
		Assert.That(station.Current.IsAvailable, Is.True, "precondition: first fetch succeeds");

		client.Forecast = new OpenMeteoForecastResponse { Current = null, Daily = null };
		await station.RefreshAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(station.Current.IsAvailable, Is.True);
			Assert.That(station.Current.Temperature, Is.EqualTo(21.0));
		});
	}

	[Test]
	public async Task RefreshAsync_reports_unavailable_when_the_first_fetch_has_no_current()
	{
		var client = new FakeOpenMeteoClient { Forecast = new OpenMeteoForecastResponse { Current = null } };
		var station = new WeatherStation(client, 0, 0, TemperatureUnit.Celsius, "Nowhere");

		await station.RefreshAsync(CancellationToken.None);

		Assert.That(station.Current.IsAvailable, Is.False);
	}

	private static OpenMeteoForecastResponse ForecastWith(double temperature)
		=> new()
		{
			Current = new OpenMeteoCurrent
			{
				Temperature = temperature,
				ApparentTemperature = temperature,
				WeatherCode = 0,
				IsDay = 1
			}
		};
}
