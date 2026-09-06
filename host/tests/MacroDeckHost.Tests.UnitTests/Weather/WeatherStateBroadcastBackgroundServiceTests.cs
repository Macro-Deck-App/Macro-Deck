using MacroDeckHost.Application.Ui.Transport;
using MacroDeckHost.Application.Ui.Transport.Messages.Weather;
using MacroDeckHost.Application.Weather;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeck.Sdk.Weather;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Core;

namespace MacroDeckHost.Tests.UnitTests.Weather;

[TestFixture]
internal sealed class WeatherStateBroadcastBackgroundServiceTests
{
	[Test]
	public async Task Tick_does_not_broadcast_an_empty_list_during_a_transient_reinit_gap()
	{
		var registry = RegistryWith("app.weather::berlin", "Berlin");
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, transport);

		await service.Tick(CancellationToken.None); // establishes [berlin]
		registry.Instances = []; // stations momentarily vanish mid-reinit
		var waitAfterEmpty = await service.Tick(CancellationToken.None); // must hold last-known-good
		registry.Instances = InstancesOf("app.weather::berlin", "Berlin"); // reinit finished
		await service.Tick(CancellationToken.None);

		var instanceBroadcasts = InstanceBroadcasts(transport);
		Assert.Multiple(() =>
		{
			Assert.That(instanceBroadcasts.Select(b => b.Instances.Count),
				Is.All.EqualTo(1),
				"the transient empty must not be pushed");
			Assert.That(waitAfterEmpty, Is.EqualTo(TimeSpan.FromSeconds(5)), "a quick recheck should be scheduled");
		});
	}

	[Test]
	public async Task Tick_re_announces_the_unchanged_list_after_a_held_back_empty()
	{
		var registry = RegistryWith("app.weather::berlin", "Berlin");
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, transport);

		await service.Tick(CancellationToken.None); // establishes [berlin]
		registry.Instances = [];
		await service.Tick(CancellationToken.None); // suspected empty -> held back, nothing pushed
		registry.Instances = InstancesOf("app.weather::berlin", "Berlin");
		await service.Tick(CancellationToken.None); // recovered

		// Clients that pulled during the hold window got the raw (empty) registry read, and the
		// change-diff would never correct them because the list never actually changed (issue #132).
		var instanceBroadcasts = InstanceBroadcasts(transport);
		Assert.Multiple(() =>
		{
			Assert.That(instanceBroadcasts, Has.Count.EqualTo(2), "the recovery must be re-announced");
			Assert.That(instanceBroadcasts[^1].Instances, Has.Count.EqualTo(1));
			Assert.That(instanceBroadcasts[^1].Instances[0].InstanceId, Is.EqualTo("app.weather::berlin"));
		});
	}

	[Test]
	public async Task Tick_broadcasts_empty_once_the_empty_is_confirmed()
	{
		var registry = RegistryWith("app.weather::berlin", "Berlin");
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, transport);

		await service.Tick(CancellationToken.None); // [berlin]
		registry.Instances = [];
		await service.Tick(CancellationToken.None); // suspected empty -> held back
		await service.Tick(CancellationToken.None); // still empty -> confirmed -> broadcast []

		var instanceBroadcasts = InstanceBroadcasts(transport);
		Assert.Multiple(() =>
		{
			Assert.That(instanceBroadcasts, Has.Count.EqualTo(2));
			Assert.That(instanceBroadcasts[^1].Instances, Is.Empty);
		});
	}

	[Test]
	public async Task Tick_does_not_re_send_an_unchanged_instance_list()
	{
		var registry = RegistryWith("app.weather::berlin", "Berlin");
		var transport = new RecordingUiTransport();
		var service = CreateService(registry, transport);

		await service.Tick(CancellationToken.None);
		await service.Tick(CancellationToken.None);

		Assert.That(InstanceBroadcasts(transport), Has.Count.EqualTo(1));
	}

	private static List<WeatherInstancesChangedNotification> InstanceBroadcasts(RecordingUiTransport transport)
		=> transport.Broadcasts.OfType<WeatherInstancesChangedNotification>().ToList();

	private static WeatherStateBroadcastBackgroundService CreateService(
		IWeatherRegistry registry,
		IUiTransport transport)
		=> new(new StartedHostLifetime(),
			registry,
			new NoOpBroadcastTrigger(),
			transport,
			new WeatherStateNotifier(),
			SilentLogger());

	private static FakeWeatherRegistry RegistryWith(string instanceId, string displayName)
	{
		var registry = new FakeWeatherRegistry { Instances = InstancesOf(instanceId, displayName) };
		registry.Stations[instanceId] = new FakeWeatherStation(displayName);
		return registry;
	}

	private static IReadOnlyList<WeatherStationDescriptor> InstancesOf(string instanceId, string displayName)
		=> [new(instanceId, "app.weather", "Open-Meteo", displayName, false)];

	private static Logger SilentLogger() => new LoggerConfiguration().CreateLogger();

	private sealed class FakeWeatherRegistry : IWeatherRegistry
	{
		public IReadOnlyList<WeatherStationDescriptor> Instances { get; set; } = [];
		public Dictionary<string, IWeatherStation> Stations { get; } = new(StringComparer.Ordinal);

		public IReadOnlyList<WeatherStationDescriptor> GetInstances() => Instances;

		public IWeatherStation? GetStation(string instanceId) => Stations.GetValueOrDefault(instanceId);

		public IWeatherStation? DefaultStation => Instances.Count > 0 ? GetStation(Instances[0].InstanceId) : null;
	}

	private sealed class NoOpBroadcastTrigger : IWeatherBroadcastTrigger
	{
		public void RequestRefresh()
		{
		}

		public Task<bool> WaitAsync(TimeSpan timeout, CancellationToken cancellationToken)
			=> Task.FromResult(false);
	}

	private sealed class StartedHostLifetime : IHostApplicationLifetime
	{
		public CancellationToken ApplicationStarted { get; } = new(true);
		public CancellationToken ApplicationStopping => CancellationToken.None;
		public CancellationToken ApplicationStopped => CancellationToken.None;

		public void StopApplication()
		{
		}
	}
}
