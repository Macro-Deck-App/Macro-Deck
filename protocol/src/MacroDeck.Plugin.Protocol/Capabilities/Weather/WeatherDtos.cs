namespace MacroDeck.Plugin.Protocol.Capabilities.Weather;

/// <summary>Mirrors the SDK's <c>WeatherStationInstance</c>.</summary>
public sealed record WeatherStationInstanceDto
{
	public required string Id { get; init; }

	public required string DisplayName { get; init; }
}

/// <summary>The full result of the <c>weather</c> capability's <c>describe</c> operation - what
/// <c>RemotePluginSnapshotRefresher</c> folds into the snapshot's provider name and instance list.</summary>
public sealed record WeatherDescribePayload
{
	public required string ProviderName { get; init; }

	public required IReadOnlyList<WeatherStationInstanceDto> Instances { get; init; }
}

/// <summary>
/// Result of the <c>instances</c> operation: the same instance list <c>describe</c> carries, exposed
/// as its own narrow round trip - see <c>MusicPlayerInstancesResult</c>'s identical remarks.
/// </summary>
public sealed record WeatherInstancesResult
{
	public required IReadOnlyList<WeatherStationInstanceDto> Instances { get; init; }
}

/// <summary>Arguments for the <c>snapshot</c> operation: which station to address. Weather station
/// instance ids are config-entry GUIDs that do not exist at declaration time - see
/// <c>ProviderCapabilityId</c>'s remarks.</summary>
public sealed record WeatherInstanceArguments
{
	public required string InstanceId { get; init; }
}

/// <summary>Mirrors the SDK's <c>WeatherForecastDay</c>. <see cref="Condition" /> is a string, not the
/// SDK's <c>WeatherCondition</c> enum - see <c>ActionParameterDto</c>'s remarks.</summary>
public sealed record WeatherForecastDayDto
{
	public required DateOnly Date { get; init; }

	/// <summary>One of the SDK's <c>WeatherCondition</c> member names.</summary>
	public required string Condition { get; init; }

	public required double Min { get; init; }

	public required double Max { get; init; }
}

/// <summary>Mirrors the SDK's <c>WeatherSnapshot</c>. <see cref="Condition" /> and <see cref="Unit" />
/// are strings, not the SDK's enums - see <c>ActionParameterDto</c>'s remarks.</summary>
public sealed record WeatherSnapshotDto
{
	public bool IsAvailable { get; init; }

	public string LocationName { get; init; } = string.Empty;

	public double? Temperature { get; init; }

	public double? ApparentTemperature { get; init; }

	/// <summary>One of the SDK's <c>WeatherCondition</c> member names.</summary>
	public required string Condition { get; init; }

	public bool IsDay { get; init; }

	/// <summary>One of the SDK's <c>TemperatureUnit</c> member names: "Celsius", "Fahrenheit".</summary>
	public required string Unit { get; init; }

	public IReadOnlyList<WeatherForecastDayDto> Days { get; init; } = [];

	/// <summary>Mirrors the SDK's <c>WeatherSnapshot.Hours</c>. Empty from a station written against the
	/// SDK before hourly data existed - a reader must handle that rather than assume it is filled.</summary>
	public IReadOnlyList<WeatherHourDto> Hours { get; init; } = [];

	public double? WindSpeed { get; init; }

	public double? WindDirection { get; init; }

	public double? Humidity { get; init; }

	public double? Precipitation { get; init; }

	public DateTimeOffset? Sunrise { get; init; }

	public DateTimeOffset? Sunset { get; init; }
}

/// <summary>Mirrors the SDK's <c>WeatherHour</c>. <see cref="Condition" /> is a string, not the SDK's
/// <c>WeatherCondition</c> enum - see <c>ActionParameterDto</c>'s remarks.</summary>
public sealed record WeatherHourDto
{
	public required DateTimeOffset Time { get; init; }

	/// <summary>One of the SDK's <c>WeatherCondition</c> member names.</summary>
	public required string Condition { get; init; }

	public required double Temperature { get; init; }

	public double? PrecipitationProbability { get; init; }
}
