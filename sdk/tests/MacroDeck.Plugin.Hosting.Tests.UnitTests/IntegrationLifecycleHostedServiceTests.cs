using MacroDeck.Plugin.Hosting.Integrations;
using MacroDeck.Plugin.Hosting.Integrations.HostApis;
using MacroDeck.Plugin.Hosting.Tests.UnitTests.Support;
using MacroDeck.Plugin.Hosting.Transport;
using MacroDeck.Plugin.Protocol.Callbacks;
using MacroDeck.Plugin.Protocol.Envelope;
using MacroDeck.Sdk;
using MacroDeck.Sdk.ConfigFlow;
using MacroDeck.Sdk.Decks;
using MacroDeck.Sdk.Events;
using MacroDeck.Sdk.Notifications;
using MacroDeck.Sdk.Scripts;
using MacroDeck.Sdk.Variables;
using MacroDeck.Sdk.Widgets;
using MacroDeck.Plugin.Testing.Fakes;
using MacroDeck.Sdk.Devices;
using MacroDeck.Sdk.Layouts;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeck.Plugin.Hosting.Tests.UnitTests;

/// <summary>
/// <see cref="IntegrationLifecycleHostedService" />'s <see cref="HostStateCache.ConfigChanged" />
/// subscription - the SDK-side half of issue #413's config-change gap. A host-pushed
/// <c>host.state{config}</c> must re-run every integration's <c>InitializeAsync</c> the same way a
/// non-resumed reconnect already does, since that is the only place a plugin picks its new config back
/// up.
/// </summary>
[TestFixture]
public class IntegrationLifecycleHostedServiceTests
{
	private static IntegrationLifecycleHostedService Service(
		TestIntegration integration,
		PluginConnectionState connectionState,
		HostStateCache stateCache)
		=> new([integration],
			TestMetadata.Default,
			new NoOpIntegrationContext(),
			new FakeDeviceProviderContext(),
			new NoOpHostInvoker(),
			new ServiceCollection().BuildServiceProvider(),
			new FakeLayoutProviderContext(),
			new FakeFolderViewProviderContext(),
			new FakeWidgetTypeProviderContext(),
			connectionState,
			stateCache,
			Serilog.Core.Logger.None);

	[Test]
	public async Task A_host_state_config_push_re_runs_InitializeAsync()
	{
		var connectionState = new PluginConnectionState();
		var stateCache = new HostStateCache(new PluginConnectionState());
		var integration = new TestIntegration();

		var service = Service(integration, connectionState, stateCache);
		await service.StartAsync(CancellationToken.None);

		connectionState.RaiseConnected(resumed: false);
		await WaitForAsync(() => integration.IsInitialized);
		Assert.That(integration.ShutdownCount, Is.Zero);

		stateCache.Apply(ConfigPush());
		await WaitForAsync(() => integration.ShutdownCount == 1);

		Assert.Multiple(() =>
		{
			// Re-initialized, not merely re-read in place: shut down once (the stale InitializeAsync
			// state discarded) and initialized a second time.
			Assert.That(integration.ShutdownCount, Is.EqualTo(1));
			Assert.That(integration.IsInitialized, Is.True);
		});

		await service.StopAsync(CancellationToken.None);
	}

	[Test]
	public async Task A_host_state_push_for_a_different_api_does_not_reinitialize()
	{
		var connectionState = new PluginConnectionState();
		var stateCache = new HostStateCache(new PluginConnectionState());
		var integration = new TestIntegration();

		var service = Service(integration, connectionState, stateCache);
		await service.StartAsync(CancellationToken.None);

		connectionState.RaiseConnected(resumed: false);
		await WaitForAsync(() => integration.IsInitialized);

		stateCache.Apply(StatePush(HostApis.Scripts));

		// Nothing to await for a negative assertion - give the (synchronous, event-driven) subscriber a
		// moment it would have used had it fired, then assert it did not.
		await Task.Delay(50);
		Assert.That(integration.ShutdownCount, Is.Zero);

		await service.StopAsync(CancellationToken.None);
	}

	[Test]
	public async Task StopAsync_unsubscribes_so_a_later_config_push_does_nothing()
	{
		var connectionState = new PluginConnectionState();
		var stateCache = new HostStateCache(new PluginConnectionState());
		var integration = new TestIntegration();

		var service = Service(integration, connectionState, stateCache);
		await service.StartAsync(CancellationToken.None);
		connectionState.RaiseConnected(resumed: false);
		await WaitForAsync(() => integration.IsInitialized);

		await service.StopAsync(CancellationToken.None);
		var shutdownsAtStop = integration.ShutdownCount;

		stateCache.Apply(ConfigPush());
		await Task.Delay(50);

		Assert.That(integration.ShutdownCount, Is.EqualTo(shutdownsAtStop));
	}

	private static ProtocolEnvelope ConfigPush() => StatePush(HostApis.Config);

	private static ProtocolEnvelope StatePush(string api)
		=> new()
		{
			Type = MessageTypes.HostState,
			Id = Guid.NewGuid().ToString(),
			Payload = FakePluginSocket.Payload(new HostStatePayload { Api = api })
		};

	[Test]
	public async Task A_device_provider_starts_with_the_integration_and_stops_before_it()
	{
		var connectionState = new PluginConnectionState();
		var stateCache = new HostStateCache(new PluginConnectionState());
		var integration = new DeviceProvidingIntegration();
		var devices = new FakeDeviceProviderContext();

		var service = new IntegrationLifecycleHostedService([integration],
			TestMetadata.Default,
			new NoOpIntegrationContext(),
			devices,
			new NoOpHostInvoker(),
			new ServiceCollection().BuildServiceProvider(),
			new FakeLayoutProviderContext(),
			new FakeFolderViewProviderContext(),
			new FakeWidgetTypeProviderContext(),
			connectionState,
			stateCache,
			Serilog.Core.Logger.None);

		await service.StartAsync(CancellationToken.None);
		connectionState.RaiseConnected(resumed: false);
		await WaitForAsync(() => integration.DeviceContext is not null);

		await service.StopAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(integration.DeviceContext, Is.SameAs(devices));
			Assert.That(integration.ProviderShutdownCount, Is.EqualTo(1));
			Assert.That(integration.ProviderStoppedBeforeIntegration,
				Is.True,
				"a provider must let go of its hardware before the integration behind it is torn down");
		});
	}

	[Test]
	public async Task A_layout_provider_starts_before_the_device_provider()
	{
		var connectionState = new PluginConnectionState();
		var stateCache = new HostStateCache(new PluginConnectionState());
		var integration = new LayoutAndDeviceProvidingIntegration();
		var devices = new FakeDeviceProviderContext();
		var layouts = new FakeLayoutProviderContext();

		var service = new IntegrationLifecycleHostedService([integration],
			TestMetadata.Default,
			new NoOpIntegrationContext(),
			devices,
			new NoOpHostInvoker(),
			new ServiceCollection().BuildServiceProvider(),
			layouts,
			new FakeFolderViewProviderContext(),
			new FakeWidgetTypeProviderContext(),
			connectionState,
			stateCache,
			Serilog.Core.Logger.None);

		await service.StartAsync(CancellationToken.None);
		connectionState.RaiseConnected(resumed: false);
		await WaitForAsync(() => integration.DeviceContext is not null);

		await service.StopAsync(CancellationToken.None);

		Assert.Multiple(() =>
		{
			Assert.That(integration.LayoutContext, Is.SameAs(layouts));
			Assert.That(integration.DeviceContext, Is.SameAs(devices));
			Assert.That(integration.LayoutInitializedBeforeDevice,
				Is.True,
				"a device may reference a layout by id, so the layout must exist before the device registers");
		});
	}

	private sealed class LayoutAndDeviceProvidingIntegration : TestIntegration, IDeviceProvider, ILayoutProvider
	{
		private bool _layoutInitialized;

		public ILayoutProviderContext? LayoutContext { get; private set; }

		public IDeviceProviderContext? DeviceContext { get; private set; }

		public bool LayoutInitializedBeforeDevice { get; private set; }

		Task ILayoutProvider.InitializeAsync(ILayoutProviderContext context, CancellationToken cancellationToken)
		{
			LayoutContext = context;
			_layoutInitialized = true;
			return Task.CompletedTask;
		}

		public Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
		{
			DeviceContext = context;
			LayoutInitializedBeforeDevice = _layoutInitialized;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(CancellationToken cancellationToken) => Task.CompletedTask;
	}

	private sealed class DeviceProvidingIntegration : TestIntegration, IDeviceProvider
	{
		public IDeviceProviderContext? DeviceContext { get; private set; }

		public int ProviderShutdownCount { get; private set; }

		public bool ProviderStoppedBeforeIntegration { get; private set; }

		public Task InitializeAsync(IDeviceProviderContext context, CancellationToken cancellationToken = default)
		{
			DeviceContext = context;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync(CancellationToken cancellationToken)
		{
			ProviderShutdownCount++;
			ProviderStoppedBeforeIntegration = ShutdownCount == 0;
			return Task.CompletedTask;
		}
	}

	private static async Task WaitForAsync(Func<bool> condition, TimeSpan? timeout = null)
	{
		var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(5));

		while (DateTime.UtcNow < deadline)
		{
			if (condition())
			{
				return;
			}

			await Task.Delay(5);
		}

		Assert.Fail("The condition was never met.");
	}

	/// <summary>Every <see cref="IIntegrationContext" /> member throwing - never exercised, since
	/// <see cref="TestIntegration.InitializeAsync" /> only records the context it was handed rather than
	/// calling through it.</summary>
	private sealed class NoOpIntegrationContext : IIntegrationContext
	{
		public IVariableApi Variables => throw new NotSupportedException();

		public IUserVariableApi UserVariables => throw new NotSupportedException();

		public IIntegrationConfig Config => throw new NotSupportedException();

		public IDeckNavigator Deck => throw new NotSupportedException();

		public IScriptApi Scripts => throw new NotSupportedException();

		public IWidgetApi Widgets => throw new NotSupportedException();

		public IEventPublisher Events => throw new NotSupportedException();

		public IUserNotifier Notifications => throw new NotSupportedException();
	}

	/// <summary>Every <see cref="IHostInvoker" /> member throwing - never exercised by a test in this
	/// file, none of which registers a variable-catalog integration.</summary>
	private sealed class NoOpHostInvoker : IHostInvoker
	{
		public Task<System.Text.Json.JsonElement?> InvokeAsync(string api,
			string operation,
			object? arguments,
			CancellationToken cancellationToken)
			=> throw new NotSupportedException();

		public bool TryComplete(ProtocolEnvelope result) => throw new NotSupportedException();
	}
}
