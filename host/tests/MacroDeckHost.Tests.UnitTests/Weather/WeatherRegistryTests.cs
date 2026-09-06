using MacroDeckHost.Application.Weather;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeck.Sdk;
using Serilog;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
public class WeatherRegistryTests
{
	private static WeatherRegistry BuildRegistry(
		IEnumerable<IIntegration> integrations,
		IEnumerable<string>? disabled = null)
		=> new(new ConfigurableIntegrationRegistry(integrations, disabled), new LoggerConfiguration().CreateLogger());

	[Test]
	public void GetInstances_PrefixesIdsWithIntegrationId()
	{
		var integration = new FakeWeatherProviderIntegration("app.weather", "Open-Meteo", "berlin", "paris");
		var registry = BuildRegistry([integration]);

		var instances = registry.GetInstances();

		Assert.Multiple(() =>
		{
			Assert.That(instances, Has.Count.EqualTo(2));
			Assert.That(instances[0].InstanceId, Is.EqualTo("app.weather::berlin"));
			Assert.That(instances[0].IntegrationId, Is.EqualTo("app.weather"));
			Assert.That(TestLocalization.Resolve(instances[0].ProviderName), Is.EqualTo("Open-Meteo"));
			Assert.That(instances[0].HasIcon, Is.True);
			Assert.That(instances[1].InstanceId, Is.EqualTo("app.weather::paris"));
		});
	}

	[Test]
	public void GetInstances_FallsBackToTheIntegrationNameWhenTheProviderStatesNoName()
	{
		var integration = new FakeWeatherProviderIntegration("app.weather", providerName: string.Empty, "berlin");
		var registry = BuildRegistry([integration]);

		Assert.That(registry.GetInstances()[0].ProviderName, Is.EqualTo(integration.Name));
	}

	[Test]
	public void GetInstances_ExcludesDisabledProviders()
	{
		var integration = new FakeWeatherProviderIntegration("app.weather", "Open-Meteo", "berlin");
		var registry = BuildRegistry([integration], disabled: ["app.weather"]);

		Assert.That(registry.GetInstances(), Is.Empty);
	}

	[Test]
	public async Task GetStation_ResolvesByGlobalId()
	{
		var integration = new FakeWeatherProviderIntegration("app.weather", "Open-Meteo", "berlin", "paris");
		var registry = BuildRegistry([integration]);

		var station = registry.GetStation("app.weather::paris");
		Assert.That(station, Is.Not.Null);

		var snapshot = await station!.GetSnapshotAsync(CancellationToken.None);
		Assert.That(snapshot.LocationName, Is.EqualTo("paris"));
	}

	[Test]
	public void GetStation_UnknownId_ReturnsNull()
	{
		var integration = new FakeWeatherProviderIntegration("app.weather", "Open-Meteo", "berlin");
		var registry = BuildRegistry([integration]);

		Assert.Multiple(() =>
		{
			Assert.That(registry.GetStation("app.weather::nope"), Is.Null);
			Assert.That(registry.GetStation("no-separator"), Is.Null);
			Assert.That(registry.GetStation("other::berlin"), Is.Null);
		});
	}

	[Test]
	public async Task Default_ReturnsFirstStation()
	{
		var integration = new FakeWeatherProviderIntegration("app.weather", "Open-Meteo", "berlin", "paris");
		var registry = BuildRegistry([integration]);

		var station = registry.DefaultStation;
		Assert.That(station, Is.Not.Null);

		var snapshot = await station!.GetSnapshotAsync(CancellationToken.None);
		Assert.That(snapshot.LocationName, Is.EqualTo("berlin"));
	}

	[Test]
	public void Default_NoInstances_ReturnsNull()
	{
		var registry = BuildRegistry([]);
		Assert.That(registry.DefaultStation, Is.Null);
	}
}
