using System.Text.Json;
using MacroDeck.Plugin.Hosting;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Batteries;

namespace MacroDeck.Plugin.Testing.Tests.UnitTests;

[TestFixture]
public class A34BatteryFakeTests
{
	private static BatteryDevice Device(string id, int level = 50)
	{
		return new BatteryDevice { Id = id, Name = id, Kind = BatteryDeviceKinds.Controller, LevelPercent = level };
	}

	[Test]
	public async Task Publishing_a_known_id_again_replaces_the_device()
	{
		var context = new FakeBatteryProviderContext();

		await context.PublishAsync(Device("pad", 90));
		await context.PublishAsync(Device("pad", 40));

		Assert.That(context.Devices.Single().Value.LevelPercent, Is.EqualTo(40));
	}

	[Test]
	public async Task A_list_that_would_exceed_the_limit_is_refused_as_a_whole()
	{
		var context = new FakeBatteryProviderContext();
		await context.PublishAsync([
			.. Enumerable.Range(0, BatteryLimits.MaxDevicesPerProvider - 1).Select(index => Device($"d{index}"))
		]);

		Assert.That(() => context.PublishAsync([Device("x"), Device("y")]), Throws.InvalidOperationException);
		Assert.That(context.Devices, Has.Count.EqualTo(BatteryLimits.MaxDevicesPerProvider - 1));
	}

	[Test]
	public async Task Removing_records_the_device_as_gone_for_good()
	{
		var context = new FakeBatteryProviderContext();
		await context.PublishAsync(Device("pad"));

		await context.RemoveAsync("pad");
		await context.RemoveAsync("never-published");

		Assert.Multiple(() =>
		{
			Assert.That(context.Devices, Is.Empty);
			Assert.That(context.RemovedIds.Single(), Is.EqualTo("pad"));
		});
	}

	[Test]
	public async Task The_harness_starts_a_battery_provider_against_its_fake_and_serves_reads_from_its_fake()
	{
		await using var harness
			= PluginTestHarness.Create(builder => builder.RegisterIntegration(_ => new PadIntegration()));
		harness.Context.Batteries.Set([
			new BatteryDeviceSnapshot
				{ Id = "adb:phone", LocalId = "phone", ProviderId = "adb", Name = "Phone", LevelPercent = 12 }
		]);

		await harness.InitializeIntegrationsAsync();

		Assert.Multiple(() =>
		{
			Assert.That(harness.Context.BatteryProvider.Devices.Keys.Single(), Is.EqualTo("pad"));
			Assert.That(((IIntegrationContext)harness.Context).Batteries.Find("adb:phone")?.LevelPercent,
				Is.EqualTo(12));
		});
	}

	[Test]
	public async Task Over_the_wire_the_test_host_accepts_what_a_battery_provider_publishes()
	{
		await using var host = await MacroDeckTestHost.StartAsync();
		await using var plugin = await host.HostAsync(MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new PadIntegration()));
		var session = await host.WaitForSessionAsync();

		await Wait.UntilAsync(() => BatteryResults(host).Any(), TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(session.Declared.Select(capability => capability.Kind),
				Does.Contain(CapabilityKinds.BatteryProvider));
			Assert.That(BatteryResults(host).Select(result => result.Error), Is.All.Null);
		});
	}

	private static IEnumerable<ProtocolEnvelope> BatteryResults(MacroDeckTestHost host)
	{
		return host.Messages.OfType(MessageTypes.HostInvoke)
			.Where(message => message.Envelope.Payload?.Deserialize<HostInvokePayload>(PluginProtocolJson.Options)?.Api
				== HostApis.Batteries)
			.SelectMany(invoke => host.Messages.WithCorrelationId(invoke.Envelope.Id))
			.Where(message => message.Envelope.Type == MessageTypes.HostResult)
			.Select(message => message.Envelope);
	}

	private sealed class PadIntegration : IPluginIntegration, IBatteryProvider
	{
		public IReadOnlyList<IActionDefinition> Actions => [];

		public Task InitializeAsync(IIntegrationContext context)
		{
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public Task InitializeAsync(IBatteryProviderContext context, CancellationToken cancellationToken = default)
		{
			return context.PublishAsync(Device("pad", 80), cancellationToken);
		}

		public Task ShutdownAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}
	}
}
