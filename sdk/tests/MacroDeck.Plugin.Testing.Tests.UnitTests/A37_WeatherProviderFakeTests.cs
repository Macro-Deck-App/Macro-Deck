using System.Text.Json;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Errors;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Weather;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A37WeatherProviderFakeTests
{
	[Test]
	public async Task The_fake_counts_every_snapshot_notice()
	{
		var api = new FakeWeatherProviderApi();

		await api.NotifySnapshotsChangedAsync();
		await api.NotifySnapshotsChangedAsync();

		Assert.That(api.SnapshotNotices, Is.EqualTo(2));
	}

	[Test]
	public async Task The_harness_records_what_a_weather_provider_reports()
	{
		await using var harness = PluginTestHarness.Create(builder
			=> builder.RegisterIntegration(_ => new StationIntegration()));

		await harness.InitializeIntegrationsAsync();

		Assert.That(harness.Context.WeatherProvider.SnapshotNotices, Is.EqualTo(1));
	}

	[Test]
	public async Task Over_the_wire_the_test_host_accepts_a_notice_from_a_weather_provider()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.HostAsync(MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new StationIntegration()));
		await host.WaitForSessionAsync();

		await Wait.UntilAsync(() => WeatherResults(host).Any(), TimeSpan.FromSeconds(10));

		Assert.That(WeatherResults(host).Select(result => result.Error), Is.All.Null);
	}

	[Test]
	public async Task Over_the_wire_a_plugin_without_the_weather_capability_is_refused_like_on_a_real_host()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.HostAsync(MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new NotifyingIntegration()));
		await host.WaitForSessionAsync();

		await Wait.UntilAsync(() => WeatherResults(host).Any(), TimeSpan.FromSeconds(10));

		Assert.That(WeatherResults(host).Select(result => result.Error?.Code),
			Is.All.EqualTo(ProtocolErrorCodes.CapabilityUnavailable));
	}

	private static IEnumerable<ProtocolEnvelope> WeatherResults(MacroDeckTestHost host)
	{
		return host.Messages.OfType(MessageTypes.HostInvoke)
			.Where(message => message.Envelope.Payload?.Deserialize<HostInvokePayload>(PluginProtocolJson.Options)?.Api
				== HostApis.Weather)
			.SelectMany(invoke => host.Messages.WithCorrelationId(invoke.Envelope.Id))
			.Where(message => message.Envelope.Type == MessageTypes.HostResult)
			.Select(message => message.Envelope);
	}

	private class NotifyingIntegration : IPluginIntegration
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return context.WeatherProvider.NotifySnapshotsChangedAsync();
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}

	private sealed class StationIntegration : NotifyingIntegration, IWeatherProvider
	{
		public IReadOnlyList<WeatherStationInstance> GetInstances()
		{
			return [];
		}

		public IWeatherStation? GetStation(string instanceId)
		{
			return null;
		}
	}
}
