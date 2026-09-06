using System.Globalization;
using MacroDeckHost.Localization;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Logging;
using MacroDeck.Sdk.Weather;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Integrations.Weather;

internal sealed class WeatherConfigFlow : IConfigFlow
{
	private static readonly ILogger _logger = IntegrationLog.For<WeatherConfigFlow>(WeatherIntegration.IntegrationId);

	private readonly IOpenMeteoClient _client;

	public WeatherConfigFlow(IOpenMeteoClient client)
	{
		_client = client;
	}

	public Task<ConfigFlowResult> StartAsync(IConfigFlowContext context, CancellationToken cancellationToken)
		=> Task.FromResult(ConfigFlowResult.Step(LocationStep()));

	public async Task<ConfigFlowResult> SubmitAsync(
		string stepId,
		IReadOnlyDictionary<string, object?> input,
		IConfigFlowContext context,
		CancellationToken cancellationToken)
	{
		if (stepId != "location")
		{
			return ConfigFlowResult.Error(LocationStep(), AppStrings.Integrations.Weather.Config.UnknownStep());
		}

		var query = (input.GetValueOrDefault("query") as string)?.Trim() ?? string.Empty;
		var manualLatitude = ToDouble(input.GetValueOrDefault("latitude"));
		var manualLongitude = ToDouble(input.GetValueOrDefault("longitude"));
		var unit = ParseUnit(input.GetValueOrDefault("unit") as string);

		double latitude;
		double longitude;
		string displayName;

		if (!string.IsNullOrWhiteSpace(query))
		{
			GeocodingResult? match;
			try
			{
				var results = await _client.SearchLocationAsync(query, cancellationToken);
				match = results.Count > 0 ? results[0] : null;
			}
			catch (Exception ex)
			{
				_logger.Warning(ex, "Geocoding lookup for '{Query}' failed", query);
				return ConfigFlowResult.Error(LocationStep(),
					AppStrings.Integrations.Weather.Config.SearchServiceUnreachable());
			}

			if (match is null)
			{
				return ConfigFlowResult.Error(LocationStep(),
					fieldErrors: new Dictionary<string, LocalizedText>
					{
						["query"] = AppStrings.Integrations.Weather.Config.NoLocationFound(query: query)
					});
			}

			latitude = match.Latitude;
			longitude = match.Longitude;
			displayName = FormatLocationName(match);
		}
		else if (manualLatitude is { } lat && manualLongitude is { } lon)
		{
			latitude = lat;
			longitude = lon;
			displayName = FormatCoordinates(lat, lon);
		}
		else
		{
			return ConfigFlowResult.Error(LocationStep(),
				AppStrings.Integrations.Weather.Config.EnterCityOrCoordinates());
		}

		var rangeErrors = new Dictionary<string, LocalizedText>();
		if (latitude is < -90 or > 90)
		{
			rangeErrors["latitude"] = AppStrings.Integrations.Weather.Config.LatitudeOutOfRange();
		}

		if (longitude is < -180 or > 180)
		{
			rangeErrors["longitude"] = AppStrings.Integrations.Weather.Config.LongitudeOutOfRange();
		}

		if (rangeErrors.Count > 0)
		{
			return ConfigFlowResult.Error(LocationStep(),
				AppStrings.Integrations.Weather.Config.CoordinatesOutOfRange(),
				rangeErrors);
		}

		var values = new Dictionary<string, ConfigFlowValue>
		{
			[WeatherConfigKeys.Latitude] = ConfigFlowValue.Plain(latitude.ToString(CultureInfo.InvariantCulture)),
			[WeatherConfigKeys.Longitude] = ConfigFlowValue.Plain(longitude.ToString(CultureInfo.InvariantCulture)),
			[WeatherConfigKeys.Unit]
				= ConfigFlowValue.Plain(unit == TemperatureUnit.Fahrenheit ? "fahrenheit" : "celsius"),
			[WeatherConfigKeys.DisplayName] = ConfigFlowValue.Plain(displayName)
		};

		return ConfigFlowResult.Complete(displayName, values);
	}

	private static ConfigFlowStep LocationStep()
		=> new()
		{
			StepId = "location",
			Title = AppStrings.Integrations.Weather.Config.LocationStepTitle(),
			Description = AppStrings.Integrations.Weather.Config.LocationStepDescription(),
			Links =
			[
				new ConfigFlowLink
				{
					Label = AppStrings.Integrations.Weather.Config.OpenMeteoAttribution(),
					Url = "https://open-meteo.com/"
				}
			],
			Fields =
			[
				ActionParameter.Text("query",
					label: AppStrings.Integrations.Weather.Config.CityLabel(),
					placeholder: AppStrings.Integrations.Weather.Config.CityPlaceholder(),
					description: AppStrings.Integrations.Weather.Config.CityDescription()),
				ActionParameter.Number("latitude",
					label: AppStrings.Integrations.Weather.Config.LatitudeLabel(),
					description: AppStrings.Integrations.Weather.Config.OnlyWithoutCityDescription(),
					min: -90,
					max: 90),
				ActionParameter.Number("longitude",
					label: AppStrings.Integrations.Weather.Config.LongitudeLabel(),
					description: AppStrings.Integrations.Weather.Config.OnlyWithoutCityDescription(),
					min: -180,
					max: 180),
				ActionParameter.Choice("unit",
					options:
					[
						new ActionParameterOption
						{
							Value = "celsius", Label = AppStrings.Integrations.Weather.Config.CelsiusOption()
						},
						new ActionParameterOption
						{
							Value = "fahrenheit", Label = AppStrings.Integrations.Weather.Config.FahrenheitOption()
						}
					],
					label: AppStrings.Integrations.Weather.Config.UnitLabel(),
					defaultValue: "celsius",
					required: true)
			]
		};

	private static string FormatLocationName(GeocodingResult result)
	{
		var parts = new List<string> { result.Name };
		if (!string.IsNullOrWhiteSpace(result.Country))
		{
			parts.Add(result.Country!);
		}

		return string.Join(", ", parts);
	}

	private static string FormatCoordinates(double latitude, double longitude)
		=> string.Format(CultureInfo.InvariantCulture,
			"{0:0.##}, {1:0.##}",
			latitude,
			longitude);

	private static TemperatureUnit ParseUnit(string? unit)
		=> string.Equals(unit, "fahrenheit", StringComparison.OrdinalIgnoreCase)
			? TemperatureUnit.Fahrenheit
			: TemperatureUnit.Celsius;

	private static double? ToDouble(object? value)
		=> value switch
		{
			null => null,
			double d => d,
			long l => l,
			int i => i,
			string s when double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
			_ => null
		};
}
