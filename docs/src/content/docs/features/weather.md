---
title: Weather
description: Expose weather stations with IWeatherProvider and IWeatherStation - cached snapshots, forecasts, units and unavailability.
---

An integration exposes weather by implementing `IWeatherProvider`. It lists one
`WeatherStationInstance` per configured location and resolves each one to an `IWeatherStation` that
returns a `WeatherSnapshot`. Map your source's own condition codes and fields onto that snapshot; the
source itself stays inside your integration.

## Quick start

```csharp
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Weather;

public sealed class RooftopWeatherIntegration : IPluginIntegration, IWeatherProvider
{
	private readonly RooftopStation _station = new(new RooftopClient("http://192.168.1.40"));

	public IReadOnlyList<IActionDefinition> Actions { get; } = [];

	public IReadOnlyList<WeatherStationInstance> GetInstances()
		=> [new WeatherStationInstance("rooftop", "Rooftop")];

	public IWeatherStation? GetStation(string instanceId)
		=> instanceId == "rooftop" ? _station : null;

	public Task InitializeAsync(IIntegrationContext context)
	{
		_station.Start();
		return Task.CompletedTask;
	}

	public Task ShutdownAsync() => _station.StopAsync();
}
```

The user can now pick "Rooftop" as the location of a **Weather widget**.

Things to know:

- **The interface gives you the widget only.** Variables such as `{{ vars.rooftop_temperature }}` come
  from your own [`IVariableProvider`](/features/variables/). The built-in Weather integration declares
  its `weather_*` variables itself, and its **Weather details** action lists only its own locations.
- **Return a cached snapshot.** The host reads every station about once a minute. Refresh on your own,
  slower schedule instead of fetching on every call.
- **Instance ids are local** (`"rooftop"`). The host qualifies them as `integrationId::rooftop` and saves
  them in widgets, so keep them stable.

## Implementing the station

```csharp
using MacroDeck.Sdk.Weather;

internal sealed class RooftopStation(RooftopClient client) : IWeatherStation
{
	private readonly CancellationTokenSource _stop = new();
	private volatile WeatherSnapshot _current = WeatherSnapshot.Unavailable("Rooftop");
	private Task _loop = Task.CompletedTask;

	public Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(_current);

	public void Start() => _loop = Task.Run(() => RefreshLoopAsync(_stop.Token));

	public async Task StopAsync()
	{
		await _stop.CancelAsync();
		await _loop;
	}

	private async Task RefreshLoopAsync(CancellationToken ct)
	{
		using var timer = new PeriodicTimer(TimeSpan.FromMinutes(10));
		do
		{
			try
			{
				var r = await client.GetReadingAsync(ct);
				_current = new WeatherSnapshot
				{
					IsAvailable = true,
					LocationName = "Rooftop",
					Unit = TemperatureUnit.Celsius,
					Temperature = r.TemperatureC,
					Condition = r.IsRaining ? WeatherCondition.Rain : WeatherCondition.Clear,
					IsDay = r.IsDaylight,
					Humidity = r.HumidityPercent,
					WindSpeed = r.WindKmh,
					WindDirection = r.WindDegrees
				};
			}
			catch (HttpRequestException)
			{
				// Keep the last good snapshot. It stays unavailable only if we never had one.
			}
		}
		while (await WaitAsync(timer, ct));
	}

	private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
	{
		try
		{
			return await timer.WaitForNextTickAsync(ct);
		}
		catch (OperationCanceledException)
		{
			return false;
		}
	}
}
```

`WeatherSnapshot.Unavailable(locationName)` is the "no data" answer: `IsAvailable = false`, with only the
location name set. Start with it and keep the last good snapshot when a refresh fails, as the built-in
Open-Meteo station does. A brief outage then doesn't blank the widget.

## The snapshot

```csharp
new WeatherSnapshot
{
	IsAvailable = true,
	LocationName = "Berlin",
	Unit = TemperatureUnit.Celsius,
	Temperature = 18.4,
	ApparentTemperature = 17.1,
	Condition = WeatherCondition.PartlyCloudy,
	IsDay = true,
	Days = [new WeatherForecastDay(new DateOnly(2026, 9, 13), WeatherCondition.Rain, Min: 11, Max: 17)],
	Hours = [new WeatherHour(DateTimeOffset.Now, WeatherCondition.RainShowers, 16.2, PrecipitationProbability: 70)]
};
```

| Property | Meaning |
| --- | --- |
| `IsAvailable` | `false` when there is no data (network error, not loaded yet). |
| `LocationName` | Shown on the widget. |
| `Unit` | `Celsius` or `Fahrenheit`. Every temperature in the snapshot is already in this unit. |
| `Temperature`, `ApparentTemperature` | Current and "feels like". `null` when unknown. |
| `Condition` | A `WeatherCondition`, used to pick the icon. |
| `IsDay` | Picks the day or night icon variant. |
| `Days` | `WeatherForecastDay(Date, Condition, Min, Max)`, one per day. |
| `Hours` | `WeatherHour(Time, Condition, Temperature, PrecipitationProbability)` from the current hour on. May be empty. |
| `WindSpeed` | km/h with `Celsius`, mph with `Fahrenheit`. |
| `WindDirection` | Degrees the wind blows **from**: 0 = north, 90 = east. |
| `Humidity` | Relative humidity, 0-100. |
| `Precipitation` | This hour: mm with `Celsius`, inches with `Fahrenheit`. |
| `Sunrise`, `Sunset` | Today, with the location's own offset. |

Everything past `Days` is optional. Leave out what your source does not report, and consumers treat it
as missing. `WeatherCondition` values: `Unknown`, `Clear`, `MainlyClear`, `PartlyCloudy`, `Overcast`,
`Fog`, `Drizzle`, `Rain`, `FreezingRain`, `Snow`, `SnowGrains`, `RainShowers`, `SnowShowers`,
`Thunderstorm`. Map anything you can't place to `Unknown`.

## Edge cases

- **A station disappears.** Drop it from `GetInstances` and return `null` from `GetStation`. Widgets
  pointing at it show as unavailable.
- **The list is briefly empty** while your integration re-initialises. The host keeps showing the last
  stations and checks again shortly before blanking anything.
- **Invalid or duplicate ids** from `GetInstances` are skipped and logged.
- **Cancellation.** Honour the token in `GetSnapshotAsync` if you ever do real work there. Returning a
  cached value, as above, never blocks.
- **`ProviderName`** is optional. Leave it out and the integration's name (the manifest name for a plugin)
  is used.

## Over the plugin protocol

Weather is fully supported out of process (capability kind `weather`). An unreachable plugin's stations
read as an unavailable snapshot. The station list is a snapshot, so after your configuration adds or
removes a location, call `CatalogChanged(CapabilityKinds.Weather)` on an injected
`IPluginCatalogNotifier`. The [weather sample](/introduction/samples-and-template/) does exactly this.
See [Capability parity](/reference/capability-parity/).

## See also

- [Variables](/features/variables/) - temperature and condition variables for templates.
- [Setup flows](/features/setup-flows/) - asking the user for a location.
- [Localization](/features/localization/)
- [Testing](/features/testing/)
