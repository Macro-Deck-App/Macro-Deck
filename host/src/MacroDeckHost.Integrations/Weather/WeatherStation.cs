using System.Globalization;
using MacroDeck.Sdk.Weather;

namespace MacroDeckHost.Integrations.Weather;

internal sealed class WeatherStation : IWeatherStation
{
	private readonly IOpenMeteoClient _client;
	private readonly double _latitude;
	private readonly double _longitude;
	private readonly TemperatureUnit _unit;

	private volatile WeatherSnapshot _current;

	public WeatherStation(
		IOpenMeteoClient client,
		double latitude,
		double longitude,
		TemperatureUnit unit,
		string displayName)
	{
		_client = client;
		_latitude = latitude;
		_longitude = longitude;
		_unit = unit;
		DisplayName = displayName;
		_current = WeatherSnapshot.Unavailable(displayName);
	}

	public string DisplayName { get; }

	public WeatherSnapshot Current => _current;

	public Task<WeatherSnapshot> GetSnapshotAsync(CancellationToken ct) => Task.FromResult(_current);

	public bool Matches(double latitude, double longitude, TemperatureUnit unit, string displayName)
		=> _latitude.Equals(latitude) &&
			_longitude.Equals(longitude) &&
			_unit == unit &&
			string.Equals(DisplayName, displayName, StringComparison.Ordinal);

	public async Task RefreshAsync(CancellationToken ct)
	{
		var response = await _client.GetForecastAsync(_latitude, _longitude, _unit, ct);
		if (response?.Current is null)
		{
			if (!_current.IsAvailable)
			{
				_current = WeatherSnapshot.Unavailable(DisplayName);
			}

			return;
		}

		_current = new WeatherSnapshot
		{
			IsAvailable = true,
			LocationName = DisplayName,
			Temperature = response.Current.Temperature,
			ApparentTemperature = response.Current.ApparentTemperature,
			Condition = WmoWeatherCodes.Map(response.Current.WeatherCode),
			IsDay = response.Current.IsDay == 1,
			Unit = _unit,
			Days = MapDays(response.Daily),
			Hours = MapHours(response.Hourly),
			WindSpeed = response.Current.WindSpeed,
			WindDirection = response.Current.WindDirection,
			Humidity = response.Current.Humidity,
			Precipitation = response.Current.Precipitation,
			Sunrise = FirstTime(response.Daily?.Sunrise),
			Sunset = FirstTime(response.Daily?.Sunset)
		};
	}

	private static List<WeatherHour> MapHours(OpenMeteoHourly? hourly)
	{
		if (hourly is null)
		{
			return [];
		}

		var count = Math.Min(hourly.Time.Count, Math.Min(hourly.Temperature.Count, hourly.WeatherCode.Count));

		var hours = new List<WeatherHour>(count);
		for (var i = 0; i < count; i++)
		{
			if (ParseLocalTime(hourly.Time[i]) is not { } time)
			{
				continue;
			}

			// Probability is a separate, shorter series when the provider has none for the far end of the
			// window, so it is indexed defensively rather than assumed to line up.
			var probability = i < hourly.PrecipitationProbability.Count
				? hourly.PrecipitationProbability[i]
				: null;

			hours.Add(new WeatherHour(time,
				WmoWeatherCodes.Map(hourly.WeatherCode[i]),
				hourly.Temperature[i],
				probability));
		}

		return hours;
	}

	private static DateTimeOffset? FirstTime(List<string>? times)
		=> times is { Count: > 0 } ? ParseLocalTime(times[0]) : null;

	// Open-Meteo returns local wall-clock times without an offset when timezone=auto. Read as unspecified
	// and stamped with no offset, so a reader sees the location's own clock rather than one shifted into
	// whatever zone this host happens to sit in.
	private static DateTimeOffset? ParseLocalTime(string value)
		=> DateTime.TryParse(value,
			CultureInfo.InvariantCulture,
			DateTimeStyles.None,
			out var parsed)
			? new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.Zero)
			: null;

	private static List<WeatherForecastDay> MapDays(OpenMeteoDaily? daily)
	{
		if (daily is null)
		{
			return [];
		}

		var count = Math.Min(daily.Time.Count,
			Math.Min(daily.TemperatureMax.Count, Math.Min(daily.TemperatureMin.Count, daily.WeatherCode.Count)));

		var days = new List<WeatherForecastDay>(count);
		for (var i = 0; i < count; i++)
		{
			if (!DateOnly.TryParse(daily.Time[i], CultureInfo.InvariantCulture, out var date))
			{
				continue;
			}

			days.Add(new WeatherForecastDay(date,
				WmoWeatherCodes.Map(daily.WeatherCode[i]),
				daily.TemperatureMin[i],
				daily.TemperatureMax[i]));
		}

		return days;
	}
}
