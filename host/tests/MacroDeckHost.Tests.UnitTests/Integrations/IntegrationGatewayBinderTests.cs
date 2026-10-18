using MacroDeckHost.Application.Messaging;
using MacroDeckHost.Application.Persistence;
using MacroDeckHost.Application.Plugins;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Variables;
using MacroDeckHost.Infrastructure.Integrations;
using MacroDeckHost.Infrastructure.Notifications;
using MacroDeckHost.Domain.Entities;
using MacroDeckHost.Application.Calendar;
using MacroDeckHost.Application.MusicPlayer;
using MacroDeckHost.Infrastructure.SystemIntegrations.Calendar;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using MacroDeck.Sdk;
using MacroDeck.Sdk.Actions;
using MacroDeck.Sdk.Variables;
using Mediator;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using MacroDeck.Localization;

namespace MacroDeckHost.Tests.UnitTests.Integrations;

[TestFixture]
internal sealed class IntegrationGatewayBinderTests
{
	[Test]
	public void Bind_gives_the_host_services_to_a_consumer_integration()
	{
		var gateway = CreateGateway();
		var integration = new GatewayConsumerIntegration();

		IntegrationGatewayBinder.Bind(integration, gateway);

		Assert.That(integration.ReceivedGateway, Is.SameAs(gateway));
	}

	[Test]
	public void Bind_does_nothing_to_an_integration_that_does_not_implement_the_marker()
	{
		var gateway = CreateGateway();
		var integration = new PlainRecordingIntegration();

		Assert.DoesNotThrow(() => IntegrationGatewayBinder.Bind(integration, gateway));
	}

	[Test]
	public async Task A_definition_refresh_requested_during_initialization_reaches_the_poller()
	{
		var gateway = CreateGateway();
		var signal = new VariableRefreshSignal();
		var integration = new VariableRequestingIntegration(variables
			=> variables.RequestDefinitionRefreshAsync(["volume-percent", "muted"]));
		using var serviceProvider = BuildScopeServices();
		var initializer = CreateInitializer(gateway, serviceProvider, refreshSignal: signal);

		await initializer.InitializeAsync(integration, "Variable Requester");

		Assert.Multiple(() =>
		{
			Assert.That(signal.DrainDefinitionsFor(integration.Id, VariableRefreshLane.Eager),
				Is.EquivalentTo(new[] { "volume-percent", "muted" }));
			Assert.That(signal.DrainEagerRefreshRequested(integration.Id), Is.False);
		});
	}

	[Test]
	public async Task Startup_binds_the_gateway_before_InitializeAsync_runs()
	{
		var gateway = CreateGateway();
		var integration = new GatewayConsumerIntegration();
		using var serviceProvider = BuildScopeServices();
		var initializer = CreateInitializer(gateway, serviceProvider);

		await initializer.InitializeAsync(integration, "Gateway Consumer");

		Assert.Multiple(() =>
		{
			Assert.That(integration.ReceivedGateway, Is.SameAs(gateway));
			Assert.That(integration.GatewayReceivedBeforeInitialize,
				Is.True,
				"the host services must be handed over before InitializeAsync");
			Assert.That(integration.InitializeCallCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Startup_leaves_a_non_consumer_integration_untouched_and_still_initializes_it()
	{
		var gateway = CreateGateway();
		var integration = new PlainRecordingIntegration();
		using var serviceProvider = BuildScopeServices();
		var initializer = CreateInitializer(gateway, serviceProvider);

		await initializer.InitializeAsync(integration, "Plain");

		Assert.That(integration.InitializeCallCount, Is.EqualTo(1));
	}

	[Test]
	public async Task Reinitialize_binds_the_gateway_before_InitializeAsync_runs()
	{
		var gateway = CreateGateway();
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
			TestScreenSaverProviders.Host(),
			TestVideoStreams.Host(),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());

		await lifecycle.ReinitializeAsync(integration.Id);

		Assert.Multiple(() =>
		{
			Assert.That(integration.ReceivedGateway, Is.SameAs(gateway));
			Assert.That(integration.GatewayReceivedBeforeInitialize,
				Is.True,
				"the host services must be handed over before InitializeAsync on the reinitialize path too");
			Assert.That(integration.InitializeCallCount, Is.EqualTo(1));
			Assert.That(integration.ShutdownCallCount, Is.EqualTo(1));
		});
	}

	[Test]
	public async Task Reinitialize_leaves_a_non_consumer_integration_untouched_and_still_initializes_it()
	{
		var gateway = CreateGateway();
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
			TestScreenSaverProviders.Host(),
			TestVideoStreams.Host(),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger());

		await lifecycle.ReinitializeAsync(integration.Id);

		Assert.That(integration.InitializeCallCount, Is.EqualTo(1));
	}

	[Test]
	public async Task Bindings_recorded_during_initialization_are_stored_for_the_calling_integration()
	{
		var gateway = CreateGateway();
		var store = new RecordingVariableBindingStore();
		var integration = new VariableRequestingIntegration(variables => variables.BindAsync([
			new VariableBindingRequest("entity/light.kitchen/state", "ha_light_kitchen", VariableType.Text)
			{
				MigratedFrom = "test/v1"
			}
		]));
		using var serviceProvider = BuildScopeServices();
		var initializer = CreateInitializer(gateway, serviceProvider, store);

		await initializer.InitializeAsync(integration, "Variable Requester");

		Assert.That(store.Saved.Select(binding => (binding.IntegrationId, binding.LocalResourceId, binding.Name,
				binding.MigratedFrom)),
			Is.EqualTo(new[]
				{ (integration.Id, "entity/light.kitchen/state", "ha_light_kitchen", (string?)"test/v1") }));
	}

	[Test]
	public async Task A_built_in_integration_reads_every_providers_music_players_and_album_colours_through_its_context()
	{
		var integration = new MusicPlayerReadingIntegration();
		using var serviceProvider = BuildScopeServices();
		var initializer = CreateInitializer(CreateGateway(),
			serviceProvider,
			musicPlayers: new OneMusicPlayer(),
			albumColor: new OneAlbumColor());

		await initializer.InitializeAsync(integration, "Music Player Reader");

		await Assert.MultipleAsync(async () =>
		{
			Assert.That(integration.Context!.MusicPlayers.Instances.Select(instance => instance.Id),
				Is.EqualTo(new[] { "net.example.jukebox::default" }));
			Assert.That(await integration.Context.MusicPlayers.GetAlbumColorAsync("net.example.jukebox::default"),
				Is.EqualTo("#102030"));
		});
	}

	private sealed class MusicPlayerReadingIntegration : IIntegration
	{
		public string Id => "test.music-player-reader";
		public LocalizedText Name => "Music Player Reader";
		public string Version => "1.0.0";
		public bool IsInitialized => Context is not null;
		public IReadOnlyList<IActionDefinition> Actions => [];
		public IIntegrationContext? Context { get; private set; }

		public Task InitializeAsync(IIntegrationContext context)
		{
			Context = context;
			return Task.CompletedTask;
		}

		public Task ShutdownAsync()
		{
			return Task.CompletedTask;
		}
	}

	private sealed class OneMusicPlayer : IMusicPlayerRegistry
	{
		public IReadOnlyList<MusicPlayerInstanceDescriptor> GetInstances()
		{
			return
			[
				new MusicPlayerInstanceDescriptor("net.example.jukebox::default",
					"net.example.jukebox",
					"Jukebox",
					"Jukebox",
					false)
			];
		}

		public MacroDeck.Sdk.MusicPlayer.IMusicPlayer? GetPlayer(string instanceId)
		{
			return null;
		}

		public MacroDeck.Sdk.MusicPlayer.IMusicPlayer? GetPlayerWithOptions(
			string instanceId,
			IReadOnlyDictionary<string, object> options)
		{
			return null;
		}

		public MacroDeck.Sdk.MusicPlayer.IMusicPlayer? DefaultPlayer => null;
	}

	private sealed class OneAlbumColor : IMusicPlayerAlbumColor
	{
		public Task<string?> GetAsync(string instanceId, CancellationToken cancellationToken = default)
		{
			return Task.FromResult<string?>("#102030");
		}
	}

	private static CalendarHostServices CreateGateway()
	{
		return new CalendarHostServices(null!, null!, TimeProvider.System, null!);
	}

	private static IntegrationInitializer CreateInitializer(
		CalendarHostServices gateway,
		ServiceProvider serviceProvider,
		IVariableBindingStore? bindingStore = null,
		IVariableRefreshSignal? refreshSignal = null,
		IMusicPlayerRegistry? musicPlayers = null,
		IMusicPlayerAlbumColor? albumColor = null)
	{
		return new IntegrationInitializer(serviceProvider.GetRequiredService<IServiceScopeFactory>(),
			new FakeDeckNavigator(),
			new FakeScriptApi(),
			new FakeWidgetApi(),
			new FakeWidgetIconInvalidator(),
			new FakeUserVariableApi(),
			new RecordingEventBus(),
			new StubEventBindingTracker(),
			new UserNotificationStore(),
			bindingStore ?? new NullVariableBindingStore(),
			refreshSignal ?? new VariableRefreshSignal(),
			new FakeIntegrationHostIssueStore(),
			TestLayoutProviders.Host(),
			TestFolderViewProviders.Host(),
			TestWidgetTypeProviders.Host(),
			TestScreenSaverProviders.Host(),
			TestVideoStreams.Host(),
			TestDeviceProviders.Host(),
			TimeProvider.System,
			new LoggerConfiguration().CreateLogger(),
			new MessageBroker(Serilog.Core.Logger.None),
			TestColors.Watches,
			calendarServices: gateway,
			albumColor: albumColor,
			musicPlayerRegistry: musicPlayers,
			musicPlayerNotifier: new MusicPlayerStateNotifier());
	}

	private static ServiceProvider BuildScopeServices()
	{
		return new ServiceCollection()
			.AddSingleton<IVariableService>(new ThrowingVariableService())
			.AddSingleton<IMediator>(new RecordingMediator())
			.AddSingleton(TestLocalization.Resolver)
			.AddSingleton(TestLocalization.Preferences)
			.BuildServiceProvider();
	}

	private sealed class VariableRequestingIntegration(Func<IVariableApi, Task> request) : IIntegration
	{
		public string Id => "test.variable-requester";
		public LocalizedText Name => "Variable Requester";
		public string Version => "1.0.0";
		public bool IsInitialized { get; private set; }
		public IReadOnlyList<IActionDefinition> Actions => [];

		public async Task InitializeAsync(IIntegrationContext context)
		{
			await request(context.Variables);
			IsInitialized = true;
		}

		public Task ShutdownAsync()
		{
			IsInitialized = false;
			return Task.CompletedTask;
		}
	}

	private sealed class GatewayConsumerIntegration : IIntegration, ICalendarHostServicesConsumer
	{
		public string Id { get; init; } = "test.gateway-consumer";
		public LocalizedText Name => "Gateway Consumer";
		public string Version => "1.0.0";
		public bool IsInitialized { get; private set; }
		public IReadOnlyList<IActionDefinition> Actions => [];
		public CalendarHostServices? ReceivedGateway { get; private set; }
		public bool GatewayReceivedBeforeInitialize { get; private set; }
		public int InitializeCallCount { get; private set; }
		public int ShutdownCallCount { get; private set; }

		public void UseCalendarServices(CalendarHostServices services)
		{
			ReceivedGateway = services;
		}

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

	private sealed class RecordingVariableBindingStore : IVariableBindingStore
	{
		public List<VariableBinding> Saved { get; } = [];

		public IReadOnlyList<VariableBinding> Load()
		{
			return [.. Saved];
		}

		public bool TryLoad(out IReadOnlyList<VariableBinding> bindings)
		{
			bindings = [.. Saved];
			return true;
		}

		public bool Save(IEnumerable<VariableBinding> bindings)
		{
			var snapshot = bindings.ToList();
			Saved.Clear();
			Saved.AddRange(snapshot);
			return true;
		}
	}

	private sealed class NullVariableBindingStore : IVariableBindingStore
	{
		public IReadOnlyList<VariableBinding> Load()
		{
			return [];
		}

		public bool TryLoad(out IReadOnlyList<VariableBinding> bindings)
		{
			bindings = [];
			return true;
		}

		public bool Save(IEnumerable<VariableBinding> bindings)
		{
			return true;
		}
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
