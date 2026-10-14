using MacroDeck.Localization;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Batteries;
using MacroDeckHost.Application.Batteries;
using MacroDeckHost.Application.Integrations;
using MacroDeckHost.Application.Messaging;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Tests.UnitTests.Integrations;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeckHost.Tests.UnitTests.VideoStreams;
using Microsoft.Extensions.DependencyInjection;

namespace MacroDeckHost.Tests.UnitTests.Batteries;

[TestFixture]
internal sealed class InProcessBatteryProviderTests
{
	private IntegrationRegistry _integrations = null!;
	private BatteryRegistry _batteries = null!;
	private IntegrationLifecycle _lifecycle = null!;
	private VideoStreamWorld _videoStreams = null!;

	[SetUp]
	public void SetUp()
	{
		var services = new ServiceCollection();
		services.AddSingleton<IVariableService>(new ThrowingVariableService());
		services.AddSingleton(TestLocalization.Resolver);
		services.AddSingleton(TestLocalization.Preferences);
		var scopeFactory = services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();

		_integrations = new IntegrationRegistry(scopeFactory,
			new IntegrationLifecycleTests.FakeIntegrationStateStore(),
			Serilog.Core.Logger.None);
		_batteries = new BatteryRegistry(TimeProvider.System, Serilog.Core.Logger.None, _integrations);
		_videoStreams = new VideoStreamWorld();
		var batteryProviders = new BatteryProviderHost(_batteries, TimeProvider.System, Serilog.Core.Logger.None);

		var initializer = new IntegrationInitializer(scopeFactory,
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new FakeWidgetIconInvalidator(),
			new FakeUserVariableApi(),
			new RecordingEventBus(),
			new StubEventBindingTracker(),
			new UserNotificationStore(),
			null!,
			new VariableRefreshSignal(),
			new FakeIntegrationHostIssueStore(),
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestScreenSaverProviders.Host(),
			TestVideoStreams.Host(_videoStreams),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			Serilog.Core.Logger.None,
			new MessageBroker(Serilog.Core.Logger.None),
			TestColors.Watches,
			batteries: _batteries,
			batteryProviders: batteryProviders);

		_lifecycle = new IntegrationLifecycle(_integrations,
			scopeFactory,
			new RecordingMediator(),
			new PluginSessionRegistry(TimeProvider.System, Serilog.Core.Logger.None),
			initializer,
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestScreenSaverProviders.Host(),
			TestVideoStreams.Host(_videoStreams),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			Serilog.Core.Logger.None,
			batteryProviders);
	}

	[TearDown]
	public void TearDown()
	{
		_batteries.Dispose();
		_videoStreams.Dispose();
	}

	[Test]
	public async Task A_started_provider_publishes_under_its_integration_and_name()
	{
		var integration = new PhoneBatteries();
		await _integrations.RegisterAsync(integration);

		await _lifecycle.ReinitializeAsync(integration.Id);

		var device = _batteries.Devices.Single();
		Assert.Multiple(() =>
		{
			Assert.That(device.Id, Is.EqualTo("com.example.phones:pixel"));
			Assert.That(device.ProviderName, Is.EqualTo("Phones"));
		});
	}

	[Test]
	public async Task Stopping_the_integration_withdraws_its_devices_and_releases_its_change_handlers()
	{
		var integration = new PhoneBatteries();
		await _integrations.RegisterAsync(integration);
		await _lifecycle.ReinitializeAsync(integration.Id);
		await integration.WaitForChangesAsync(1);

		await _lifecycle.ShutdownAsync(integration.Id);
		await Task.Delay(200);
		var afterShutdown = integration.Changes;
		_batteries.Publish("com.example.other", null, [new BatteryDevice { Id = "watch", Name = "Watch" }]);
		await Task.Delay(200);

		Assert.Multiple(() =>
		{
			Assert.That(_batteries.Devices.Select(device => device.ProviderId),
				Is.EqualTo(new[] { "com.example.other" }));
			Assert.That(integration.Changes,
				Is.EqualTo(afterShutdown),
				"a handler subscribed through the context outlived its integration");
		});
	}

	[Test]
	public async Task Initializing_again_replaces_the_previous_initializations_handlers()
	{
		var integration = new PhoneBatteries();
		await _integrations.RegisterAsync(integration);
		await _lifecycle.ReinitializeAsync(integration.Id);
		await _lifecycle.ReinitializeAsync(integration.Id);
		await Task.Delay(200);
		var before = integration.Changes;

		_batteries.Publish("com.example.other", null, [new BatteryDevice { Id = "watch", Name = "Watch" }]);
		await integration.WaitForChangesAsync(before + 1);
		await Task.Delay(200);

		Assert.That(integration.Changes, Is.EqualTo(before + 1));
	}

	private sealed class PhoneBatteries : IIntegration, IBatteryProvider
	{
		private int _changes;

		public string Id => "com.example.phones";

		public LocalizedText Name => "Phones";

		public string Version => "1.0.0";

		public IReadOnlyList<IActionDefinition> Actions { get; } = [];

		public bool IsInitialized => true;

		public int Changes => Volatile.Read(ref _changes);

		public Task InitializeAsync(IIntegrationContext context)
		{
			context.Batteries.Changed += (_, _) => Interlocked.Increment(ref _changes);
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}

		public Task InitializeAsync(IBatteryProviderContext context, CancellationToken cancellationToken = default)
		{
			return context.PublishAsync(new BatteryDevice { Id = "pixel", Name = "Pixel", LevelPercent = 50 },
				cancellationToken);
		}

		public Task ShutdownAsync(CancellationToken cancellationToken = default)
		{
			return Task.CompletedTask;
		}

		public async Task WaitForChangesAsync(int count)
		{
			var deadline = DateTime.UtcNow.AddSeconds(5);
			while (DateTime.UtcNow < deadline && Changes < count)
			{
				await Task.Delay(10);
			}

			Assert.That(Changes, Is.GreaterThanOrEqualTo(count), "the battery change never reached the integration");
		}
	}
}
