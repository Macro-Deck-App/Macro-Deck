using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.Adb;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Integrations;
using MacroDeckHost.Integrations.Adb;
using MacroDeckHost.Integrations.HomeAssistant;
using MacroDeckHost.Tests.UnitTests.Adb;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationGatewayBinderTests
{
	[Test]
	public void Bind_gives_the_gateway_to_a_consumer_integration()
	{
		using var gateway = CreateGateway();
		var integration = new GatewayConsumerIntegration();

		IntegrationGatewayBinder.Bind(integration,
			gateway,
			null!,
			new NullVariableBindingStore(),
			new VariableRefreshSignal());

		Assert.That(integration.ReceivedGateway, Is.SameAs(gateway));
	}

	[Test]
	public void Bind_does_nothing_to_an_integration_that_does_not_implement_the_marker()
	{
		using var gateway = CreateGateway();
		var integration = new PlainRecordingIntegration();

		Assert.DoesNotThrow(() => IntegrationGatewayBinder.Bind(integration,
			gateway,
			null!,
			new NullVariableBindingStore(),
			new VariableRefreshSignal()));
	}

	[Test]
	public void Bind_gives_the_variable_refresh_signal_to_a_consumer_integration()
	{
		using var gateway = CreateGateway();
		var signal = new VariableRefreshSignal();
		var integration = new RefreshSignalConsumerIntegration();

		IntegrationGatewayBinder.Bind(integration, gateway, null!, new NullVariableBindingStore(), signal);

		Assert.That(integration.ReceivedSignal, Is.SameAs(signal));
	}

	[Test]
	public async Task Startup_binds_the_variable_refresh_signal_before_InitializeAsync_runs()
	{
		using var gateway = CreateGateway();
		var integration = new RefreshSignalConsumerIntegration();
		using var serviceProvider = BuildScopeServices();
		var initializer = CreateInitializer(gateway, serviceProvider);

		await initializer.InitializeAsync(integration, "Refresh Signal Consumer");

		Assert.Multiple(() =>
		{
			Assert.That(integration.ReceivedSignal, Is.Not.Null);
			Assert.That(integration.SignalReceivedBeforeInitialize,
				Is.True,
				"UseVariableRefreshSignal must run before InitializeAsync, because an integration wires its " +
				"connections up during initialization");
		});
	}

	[Test]
	public async Task Startup_binds_the_gateway_before_InitializeAsync_runs()
	{
		using var gateway = CreateGateway();
		var integration = new GatewayConsumerIntegration();
		using var serviceProvider = BuildScopeServices();
		var initializer = CreateInitializer(gateway, serviceProvider);

		await initializer.InitializeAsync(integration, "Gateway Consumer");

		Assert.Multiple(() =>
		{
			Assert.That(integration.ReceivedGateway, Is.SameAs(gateway));
			Assert.That(integration.GatewayReceivedBeforeInitialize,
				Is.True,
				"UseGateway must run before InitializeAsync");
			Assert.That(integration.InitializeCallCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Startup_leaves_a_non_consumer_integration_untouched_and_still_initializes_it()
	{
		using var gateway = CreateGateway();
		var integration = new PlainRecordingIntegration();
		using var serviceProvider = BuildScopeServices();
		var initializer = CreateInitializer(gateway, serviceProvider);

		await initializer.InitializeAsync(integration, "Plain");

		Assert.That(integration.InitializeCallCount, Is.EqualTo(1));
	}

	[Test]
	public async Task Reinitialize_binds_the_gateway_before_InitializeAsync_runs()
	{
		using var gateway = CreateGateway();
		var integration = new GatewayConsumerIntegration();
		using var serviceProvider = BuildScopeServices();
		var lifecycle = new IntegrationLifecycle(new ConfigurableIntegrationRegistry([integration]),
			serviceProvider.GetRequiredService<IServiceScopeFactory>(),
			new RecordingMediator(),
			new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None),
			CreateInitializer(gateway, serviceProvider),
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());

		await lifecycle.ReinitializeAsync(integration.Id);

		Assert.Multiple(() =>
		{
			Assert.That(integration.ReceivedGateway, Is.SameAs(gateway));
			Assert.That(integration.GatewayReceivedBeforeInitialize,
				Is.True,
				"UseGateway must run before InitializeAsync on the reinitialize path too");
			Assert.That(integration.InitializeCallCount, Is.EqualTo(1));
			Assert.That(integration.ShutdownCallCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Reinitialize_leaves_a_non_consumer_integration_untouched_and_still_initializes_it()
	{
		using var gateway = CreateGateway();
		var integration = new PlainRecordingIntegration();
		using var serviceProvider = BuildScopeServices();
		var lifecycle = new IntegrationLifecycle(new ConfigurableIntegrationRegistry([integration]),
			serviceProvider.GetRequiredService<IServiceScopeFactory>(),
			new RecordingMediator(),
			new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None),
			CreateInitializer(gateway, serviceProvider),
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());

		await lifecycle.ReinitializeAsync(integration.Id);

		Assert.That(integration.InitializeCallCount, Is.EqualTo(1));
	}

	/// <summary>
	/// The Home Assistant watched-entity migration reaches the binding store only through this seam, and
	/// it must run before InitializeAsync or the migration has nothing to write into - which is exactly
	/// how it shipped unwired once already.
	/// </summary>
	[Test]
	public void Bind_gives_the_binding_store_to_a_consumer_integration()
	{
		using var gateway = CreateGateway();
		var store = new NullVariableBindingStore();
		var integration = new BindingStoreConsumerIntegration();

		IntegrationGatewayBinder.Bind(integration, gateway, null!, store, new VariableRefreshSignal());

		Assert.That(integration.ReceivedStore, Is.SameAs(store));
	}

	[Test]
	public async Task Startup_binds_the_binding_store_before_InitializeAsync_runs()
	{
		using var gateway = CreateGateway();
		var integration = new BindingStoreConsumerIntegration();
		using var serviceProvider = BuildScopeServices();
		var initializer = CreateInitializer(gateway, serviceProvider);

		await initializer.InitializeAsync(integration, "Binding Store Consumer");

		Assert.Multiple(() =>
		{
			Assert.That(integration.ReceivedStore, Is.Not.Null);
			Assert.That(integration.StoreReceivedBeforeInitialize,
				Is.True,
				"UseBindingStore must run before InitializeAsync");
		});
	}

	private static AdbGateway CreateGateway() => new(new FakeAdbManager(), new LoggerConfiguration().CreateLogger());

	private static IntegrationInitializer CreateInitializer(IAdbGateway gateway, ServiceProvider serviceProvider)
		=> new(serviceProvider.GetRequiredService<IServiceScopeFactory>(),
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new FakeWidgetIconInvalidator(),
			new FakeUserVariableApi(),
			new RecordingEventBus(),
			new UserNotificationStore(),
			gateway,
			null!,
			new NullVariableBindingStore(),
			new VariableRefreshSignal(),
			new FakeIntegrationHostIssueStore(),
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());

	private static ServiceProvider BuildScopeServices()
		=> new ServiceCollection()
			.AddSingleton<IVariableService>(new ThrowingVariableService())
			.AddSingleton<IMediator>(new RecordingMediator())
			.AddSingleton(TestLocalization.Resolver)
			.AddSingleton(TestLocalization.Preferences)
			.BuildServiceProvider();

	private sealed class RefreshSignalConsumerIntegration : IIntegration, IVariableRefreshSignalConsumer
	{
		public string Id => "test.refresh-signal-consumer";
		public LocalizedText Name => "Refresh Signal Consumer";
		public string Version => "1.0.0";
		public bool IsInitialized { get; private set; }
		public IReadOnlyList<IActionDefinition> Actions => [];
		public IVariableRefreshSignal? ReceivedSignal { get; private set; }
		public bool SignalReceivedBeforeInitialize { get; private set; }

		public void UseVariableRefreshSignal(IVariableRefreshSignal signal) => ReceivedSignal = signal;

		public Task InitializeAsync(IIntegrationContext context)
		{
			SignalReceivedBeforeInitialize = ReceivedSignal is not null;
			IsInitialized = true;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			IsInitialized = false;
			return Task.CompletedTask;
		}
	}

	private sealed class GatewayConsumerIntegration : IIntegration, IAdbGatewayConsumer
	{
		public string Id { get; init; } = "test.gateway-consumer";
		public LocalizedText Name => "Gateway Consumer";
		public string Version => "1.0.0";
		public bool IsInitialized { get; private set; }
		public IReadOnlyList<IActionDefinition> Actions => [];
		public IAdbGateway? ReceivedGateway { get; private set; }
		public bool GatewayReceivedBeforeInitialize { get; private set; }
		public int InitializeCallCount { get; private set; }
		public int ShutdownCallCount { get; private set; }

		public void UseGateway(IAdbGateway gateway) => ReceivedGateway = gateway;

		public Task InitializeAsync(IIntegrationContext context)
		{
			InitializeCallCount++;
			GatewayReceivedBeforeInitialize = ReceivedGateway is not null;
			IsInitialized = true;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			ShutdownCallCount++;
			IsInitialized = false;
			return Task.CompletedTask;
		}
	}

	private sealed class BindingStoreConsumerIntegration : IIntegration, IHomeAssistantBindingStoreConsumer
	{
		public string Id { get; init; } = "test.binding-store-consumer";
		public LocalizedText Name => "Binding Store Consumer";
		public string Version => "1.0.0";
		public bool IsInitialized { get; private set; }
		public IReadOnlyList<IActionDefinition> Actions => [];
		public IVariableBindingStore? ReceivedStore { get; private set; }
		public bool StoreReceivedBeforeInitialize { get; private set; }

		public void UseBindingStore(IVariableBindingStore store) => ReceivedStore = store;

		public Task InitializeAsync(IIntegrationContext context)
		{
			StoreReceivedBeforeInitialize = ReceivedStore is not null;
			IsInitialized = true;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			IsInitialized = false;
			return Task.CompletedTask;
		}
	}

	private sealed class NullVariableBindingStore : IVariableBindingStore
	{
		public IReadOnlyList<VariableBinding> Load() => [];

		public bool TryLoad(out IReadOnlyList<VariableBinding> bindings)
		{
			bindings = [];
			return true;
		}

		public bool Save(IEnumerable<VariableBinding> bindings) => true;
	}

	private sealed class PlainRecordingIntegration : IIntegration
	{
		public string Id { get; init; } = "test.plain";
		public LocalizedText Name => "Plain";
		public string Version => "1.0.0";
		public bool IsInitialized { get; private set; }
		public IReadOnlyList<IActionDefinition> Actions => [];
		public int InitializeCallCount { get; private set; }

		public Task InitializeAsync(IIntegrationContext context)
		{
			InitializeCallCount++;
			IsInitialized = true;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			IsInitialized = false;
			return Task.CompletedTask;
		}
	}
}
