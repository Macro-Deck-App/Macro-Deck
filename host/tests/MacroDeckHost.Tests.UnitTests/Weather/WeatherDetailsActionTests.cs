using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Weather;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Integrations.Weather;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Weather;

/// <summary>
/// The detail action names a station that the dialog later resolves through <see cref="WeatherRegistry" />,
/// and the two must agree on which id space that name lives in. They did not: the action offered the
/// integration-local id while the registry keys stations by their qualified id, so picking a location
/// produced a value nothing could look up and the dialog showed its no-location empty state - on a
/// location the user had explicitly chosen. Leaving the parameter unset kept working, because that path
/// falls back to a descriptor whose id is already qualified, which is what hid it.
/// </summary>
[TestFixture]
public class WeatherDetailsActionTests
{
	private const string LocationParameter = "instanceId";

	[Test]
	public async Task Every_offered_location_resolves_to_a_station()
	{
		var integration = new FakeWeatherProviderIntegration(WeatherIntegration.IntegrationId,
			"Open-Meteo",
			"berlin",
			"paris");
		var registry = new WeatherRegistry(new ConfigurableIntegrationRegistry([integration]),
			new LoggerConfiguration().CreateLogger());
		var action = new WeatherDetailsActionDefinition(() =>
			[new WeatherStationInstance("berlin", "Berlin"), new WeatherStationInstance("paris", "Paris")]);

		var options = await action.GetDynamicOptionsAsync(Context(), CancellationToken.None);

		Assert.That(options.Options, Has.Count.EqualTo(2), "The action offered no locations to resolve.");
		Assert.Multiple(() =>
		{
			foreach (var option in options.Options)
			{
				Assert.That(registry.GetStation(option.Value),
					Is.Not.Null,
					$"The dialog cannot resolve the location '{option.Value}' this action offers.");
			}
		});
	}

	/// <summary>The label stays the station's own display name - the value changing id space must not
	/// leak a qualified id into what the user reads.</summary>
	[Test]
	public async Task An_offered_location_reads_as_its_display_name()
	{
		var action = new WeatherDetailsActionDefinition(() => [new WeatherStationInstance("berlin", "Berlin")]);

		var options = await action.GetDynamicOptionsAsync(Context(), CancellationToken.None);

		Assert.That(TestLocalization.Resolve(options.Options[0].Label), Is.EqualTo("Berlin"));
	}

	private static DynamicOptionsContext Context()
		=> new() { ParameterName = LocationParameter, CurrentParameters = new Dictionary<string, object?>() };
}
