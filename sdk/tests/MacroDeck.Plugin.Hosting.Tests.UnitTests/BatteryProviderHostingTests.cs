using System.Text.Json;
using MacroDeck.Plugin.Hosting.Capabilities;
using MacroDeck.Plugin.Hosting.Capabilities.BatteryProvider;
using MacroDeck.Plugin.Hosting.Integrations;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Capabilities;
using MacroDeck.Plugin.Protocol.Capabilities.BatteryProvider;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Plugin.Protocol.Handshake;
using MacroDeck.Plugin.Protocol.Serialization;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Batteries;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

[TestFixture]
public class BatteryProviderHostingTests
{
	[Test]
	public void A_plugin_with_a_battery_provider_declares_the_capability_and_one_without_does_not()
	{
		using var withProvider = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestBatteryIntegration())
			.Build();
		using var withoutProvider = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestIntegration())
			.Build();

		Assert.Multiple(() =>
		{
			Assert.That(DeclaredKinds(withProvider), Does.Contain(CapabilityKinds.BatteryProvider));
			Assert.That(DeclaredKinds(withoutProvider), Does.Not.Contain(CapabilityKinds.BatteryProvider));
		});
	}

	[Test]
	public async Task Describe_names_the_provider_or_falls_back_to_the_plugin_name()
	{
		var named = new BatteryProviderCapabilityHandler([new TestBatteryIntegration { ProviderName = "Buds" }],
			TestMetadata.Default);
		var unnamed = new BatteryProviderCapabilityHandler([new TestBatteryIntegration()], TestMetadata.Default);

		var namedName = await DescribeAsync(named);
		var unnamedName = await DescribeAsync(unnamed);

		Assert.Multiple(() =>
		{
			Assert.That(namedName, Is.EqualTo("Buds"));
			Assert.That(unnamedName, Is.EqualTo(TestMetadata.Default.Name));
		});
	}

	[Test]
	public async Task A_battery_provider_starts_after_its_integration_and_stops_before_it()
	{
		var connectionState = new PluginConnectionState();
		var batteries = new FakeBatteryProviderContext();
		var integration = new TestBatteryIntegration
		{
			OnInitialize = context => context.PublishAsync(new BatteryDevice { Id = "left", Name = "Left bud" })
		};
		var service = Lifecycle(integration,
			new ServiceCollection().AddSingleton<IBatteryProviderContext>(batteries).BuildServiceProvider(),
			connectionState);

		await service.StartAsync(CancellationToken.None);
		connectionState.RaiseConnected(false);
		await WaitForAsync(() => integration.BatteryContext is not null);
		var wasInitializedFirst = integration.IsInitialized;
		await service.StopAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(wasInitializedFirst, Is.True);
			Assert.That(integration.BatteryContext, Is.SameAs(batteries));
			Assert.That(batteries.Devices.Keys, Is.EqualTo(new[] { "left" }));
			Assert.That(integration.ProviderShutdownCount, Is.EqualTo(1));
			Assert.That(integration.ProviderStoppedBeforeIntegration, Is.True);
		});
	}

	[Test]
	public async Task
		Changed_handlers_subscribed_through_the_context_are_released_when_the_integration_is_initialized_again()
	{
		var connectionState = new PluginConnectionState();
		var stateCache = new HostStateCache(new PluginConnectionState());
		using var api = new RemoteBatteryApi(stateCache, Serilog.Core.Logger.None);
		var calls = 0;
		var integration = new TestIntegration();
		var service = Lifecycle(integration,
			new ServiceCollection().AddSingleton(api).BuildServiceProvider(),
			connectionState,
			stateCache);
		await service.StartAsync(CancellationToken.None);
		connectionState.RaiseConnected(false);
		await WaitForAsync(() => integration.IsInitialized);
		api.Lifecycle.Changed += (_, _) => Interlocked.Increment(ref calls);

		stateCache.Apply(new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = FakePluginSocket.Payload(new Protocol.Callbacks.HostStatePayload
				{ Api = Protocol.Callbacks.HostApis.Config })
		});
		await WaitForAsync(() => integration.ShutdownCount == 1 && integration.IsInitialized);
		stateCache.Apply(new ProtocolEnvelope
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = FakePluginSocket.Payload(new Protocol.Callbacks.HostStatePayload
			{
				Api = Protocol.Callbacks.HostApis.Batteries,
				Data = JsonSerializer.SerializeToElement(new Protocol.Callbacks.BatteryStateDto { Revision = 1 },
					PluginProtocolJson.Options)
			})
		});
		await Task.Delay(100);
		await service.StopAsync(CancellationToken.None);

		Assert.That(Volatile.Read(ref calls), Is.Zero);
	}

	[Test]
	public void The_integration_context_of_a_built_plugin_reads_the_batteries_the_host_pushes()
	{
		using var plugin = MacroDeckPlugin.CreatePlugin()
			.RegisterIntegration(_ => new TestIntegration())
			.Build();
		var context = plugin.Services.GetRequiredService<IIntegrationContext>();

		plugin.Services.GetRequiredService<HostStateCache>()
			.Apply(new ProtocolEnvelope
			{
				Type = MessageTypes.HostState,
				Id = Guid.NewGuid().ToString(),
				Payload = FakePluginSocket.Payload(new Protocol.Callbacks.HostStatePayload
				{
					Api = Protocol.Callbacks.HostApis.Batteries,
					Data = JsonSerializer.SerializeToElement(new Protocol.Callbacks.BatteryStateDto
						{
							Revision = 1,
							Devices =
							[
								new Protocol.Callbacks.BatteryDeviceSnapshotDto
								{
									Id = "adb:pixel", LocalId = "pixel", ProviderId = "adb", Name = "Pixel",
									LevelPercent = 77
								}
							]
						},
						PluginProtocolJson.Options)
				})
			});

		Assert.That(context.Batteries.Find("adb:pixel")?.LevelPercent, Is.EqualTo(77));
	}

	private static IntegrationLifecycleHostedService Lifecycle(
		IPluginIntegration integration,
		IServiceProvider services,
		PluginConnectionState connectionState,
		HostStateCache? stateCache = null)
	{
		return new IntegrationLifecycleHostedService([integration],
			TestMetadata.Default,
			new FakeIntegrationContext(),
			new FakeDeviceProviderContext(),
			new ThrowingHostInvoker(),
			services,
			new FakeLayoutProviderContext(),
			new FakeFolderViewProviderContext(),
			new FakeWidgetTypeProviderContext(),
			new FakeScreenSaverProviderContext(),
			connectionState,
			stateCache ?? new HostStateCache(new PluginConnectionState()),
			Serilog.Core.Logger.None);
	}

	private static IReadOnlyList<string> DeclaredKinds(PluginApplication application)
	{
		return
		[
			.. application.Services.GetServices<ICapabilityHandler>()
				.SelectMany(handler => handler.DeclareCapabilities())
				.Select(capability => capability.Kind)
		];
	}

	private static async Task<string> DescribeAsync(BatteryProviderCapabilityHandler handler)
	{
		var result = await handler.InvokeAsync(new CapabilityInvocation
			{
				Kind = CapabilityKinds.BatteryProvider,
				LocalId = ProviderCapabilityId.LocalId,
				Operation = CapabilityOperations.BatteryProvider.Describe,
				CorrelationId = "c1",
				Services = new ServiceCollection().BuildServiceProvider()
			},
			CancellationToken.None);

		return result.Data!.Value.Deserialize<BatteryProviderDescribePayload>(PluginProtocolJson.Options)!.ProviderName;
	}

	private static async Task WaitForAsync(Func<bool> condition)
	{
		var deadline = DateTime.UtcNow.AddSeconds(5);
		while (!condition())
		{
			if (DateTime.UtcNow > deadline)
			{
				Assert.Fail("The expected state was never reached.");
			}

			await Task.Delay(5);
		}
	}

	private sealed class ThrowingHostInvoker : IHostInvoker
	{
		public Task<JsonElement?> InvokeAsync(
			string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
		{
			throw new NotSupportedException();
		}

		public bool TryComplete(ProtocolEnvelope result)
		{
			return false;
		}
	}
}
