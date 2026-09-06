using MacroDeckHost.Application.Ui.Handlers;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
public class WeatherHandlersTests
{
	private static readonly ILogger _logger = Log.Logger;

	private static WeatherRegistry BuildRegistry(params IIntegration[] integrations)
		=> new(new ConfigurableIntegrationRegistry(integrations), _logger);

	[Test]
	public async Task GetWeatherInstances_MapsDescriptors()
	{
		var registry
			= BuildRegistry(new FakeWeatherProviderIntegration("app.weather", "Open-Meteo", "berlin", "paris"));
		var handler = new GetWeatherInstancesRequestMessageHandler(registry);

		var response = await handler.Handle(new GetWeatherInstancesRequest(), CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.Instances, Has.Count.EqualTo(2));
			Assert.That(response.Instances[0].InstanceId, Is.EqualTo("app.weather::berlin"));
			Assert.That(response.Instances[0].HasIcon, Is.True);
		});
	}

	[Test]
	public async Task GetWeatherState_WithInstanceId_ReturnsThatStation()
	{
		var registry
			= BuildRegistry(new FakeWeatherProviderIntegration("app.weather", "Open-Meteo", "berlin", "paris"));
		var handler = new GetWeatherStateRequestMessageHandler(registry, _logger);

		var response = await handler.Handle(new GetWeatherStateRequest { InstanceId = "app.weather::paris" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.State.IsAvailable, Is.True);
			Assert.That(response.State.LocationName, Is.EqualTo("paris"));
			Assert.That(response.State.InstanceId, Is.EqualTo("app.weather::paris"));
		});
	}

	[Test]
	public async Task GetWeatherState_NullInstanceId_FallsBackToFirst()
	{
		var registry
			= BuildRegistry(new FakeWeatherProviderIntegration("app.weather", "Open-Meteo", "berlin", "paris"));
		var handler = new GetWeatherStateRequestMessageHandler(registry, _logger);

		var response = await handler.Handle(new GetWeatherStateRequest { InstanceId = null }, CancellationToken.None);

		Assert.That(response.State.LocationName, Is.EqualTo("berlin"));
	}

	[Test]
	public async Task GetWeatherState_UnknownInstanceId_ReturnsUnavailable()
	{
		var registry = BuildRegistry(new FakeWeatherProviderIntegration("app.weather", "Open-Meteo", "berlin"));
		var handler = new GetWeatherStateRequestMessageHandler(registry, _logger);

		var response = await handler.Handle(new GetWeatherStateRequest { InstanceId = "app.weather::nope" },
			CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(response.State.IsAvailable, Is.False);

			// A widget bound to a removed location renders from this payload, so it has to say that the
			// station is gone rather than look like a snapshot that has not arrived yet (issue #132).
			Assert.That(response.State.StationExists, Is.False);
		});
	}

	[Test]
	public async Task GetWeatherState_KnownStationWithoutSnapshot_StillReportsTheStationExists()
	{
		var registry = BuildRegistry(new FakeWeatherProviderIntegration("app.weather", "Open-Meteo", "berlin"));
		var handler = new GetWeatherStateRequestMessageHandler(registry, _logger);

		var response = await handler.Handle(new GetWeatherStateRequest { InstanceId = "app.weather::berlin" },
			CancellationToken.None);

		Assert.That(response.State.StationExists, Is.True);
	}
}
