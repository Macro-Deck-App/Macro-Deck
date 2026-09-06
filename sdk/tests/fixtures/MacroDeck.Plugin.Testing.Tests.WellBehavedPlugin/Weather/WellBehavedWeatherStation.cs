using MacroDeck.Sdk.Weather;

namespace MacroDeck.Plugin.Testing.Tests.WellBehavedPlugin.Weather;

/// <summary>
/// The fixture's single synthetic weather station. Needs no external service: <see cref="Tick"/>
/// computes a plausible reading from a running counter and caches it, and <see cref="GetSnapshotAsync"/>
/// just hands that cached value back - the "synchronous-enumeration/snapshot" shape
/// <see cref="IWeatherStation"/> asks every provider for, so a widget reading the station never waits
/// on I/O this fixture does not have.
/// </summary>
internal sealed class WellBehavedWeatherStation(WellBehavedIntegration integration) : IWeatherStation
{
	private int _tickCount;
	private WeatherCondition? _forcedCondition;

	/// <summary>The most recently computed reading. Exposed synchronously so the temperature variable
	/// can echo the same value <see cref="GetSnapshotAsync"/> serves, without a second round of I/O.</summary>
	internal WeatherSnapshot LastSnapshot { get; private set; } = WeatherSnapshot.Unavailable();

	public Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(LastSnapshot);

	/// <summary>Overrides the condition the next <see cref="Tick"/> reports - what the "force weather
	/// condition" action drives, so a demo deck can show every icon the Weather widget knows on demand.</summary>
	internal void SetForcedCondition(WeatherCondition condition) => _forcedCondition = condition;

	/// <summary>
	/// Advances the synthetic reading by one step, caches it, and republishes the plugin's
	/// <c>weather-refreshed</c> event. Called by the "refresh weather" action and once from
	/// <see cref="WellBehavedIntegration.InitializeAsync"/> to seed a first reading. A real provider would
	/// instead poll an external API on its own slow interval and cache the result here - see
	/// <see cref="IWeatherStation"/>'s remarks on why fetching on every call is the wrong shape.
	/// </summary>
	internal void Tick()
	{
		_tickCount++;

		// A gentle sine wave rather than randomness, so a demo deck shows a believable rise and fall
		// instead of jumping around - and so this fixture's own tests get a deterministic reading.
		var temperature = Math.Round(15 + 6 * Math.Sin(_tickCount * 0.5), 1);
		var condition = _forcedCondition ??
			(temperature switch
			{
				< 0 => WeatherCondition.Snow,
				< 12 => WeatherCondition.Overcast,
				< 20 => WeatherCondition.PartlyCloudy,
				_ => WeatherCondition.Clear
			});

		var today = DateOnly.FromDateTime(DateTime.UtcNow);
		LastSnapshot = new WeatherSnapshot
		{
			IsAvailable = true,
			LocationName = integration.LocationName,
			Temperature = temperature,
			ApparentTemperature = temperature - 1,
			Condition = condition,
			IsDay = DateTimeOffset.UtcNow.Hour is >= 6 and < 20,
			Unit = TemperatureUnit.Celsius,
			Days = [new WeatherForecastDay(today, condition, temperature - 4, temperature + 4)]
		};

		integration.PublishWeatherRefreshed(LastSnapshot, isAlert: temperature >= integration.AlertThresholdCelsius);
	}
}
