using System.Collections.Concurrent;
using System.Text.Json;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Events;
using MacroDeckHost.Application.Events.Handlers;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.Events;

[TestFixture]
public sealed class IntegrationStateChangedEventTriggerHandlerTests
{
	private const string IntegrationId = "app.test.obs";
	private const string Connected = "macro-deck::integration-connected";
	private const string Disconnected = "macro-deck::integration-disconnected";

	private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(300);

	[Test]
	public async Task An_integration_initialized_before_event_dispatch_is_ready_runs_its_connected_automation_once()
	{
		await using var world = await World.Start();
		var automation = world.Automations.Add(Flows("integration-connected", IntegrationId));

		await world.Handler.Handle(new IntegrationStateChangedNotification(IntegrationId), CancellationToken.None);
		world.MarkStartupReady();

		await world.Runner.FirstRun.WaitAsync(TimeSpan.FromSeconds(10));
		await Task.Delay(_settle);

		Assert.That(world.Runner.Runs,
			Is.EqualTo(new[] { (EventTriggerOwner.ForAutomation(automation.Id), Connected, IntegrationId) }));
	}

	[Test]
	public async Task A_startup_connect_stays_connected_when_the_integration_is_disabled_before_event_dispatch_is_ready()
	{
		await using var world = await World.Start();
		var connected = world.Automations.Add(Flows("integration-connected", IntegrationId));
		world.Automations.Add(Flows("integration-disconnected", IntegrationId));

		await world.Handler.Handle(new IntegrationStateChangedNotification(IntegrationId), CancellationToken.None);
		world.Registry.SetEnabled(IntegrationId, false);
		world.MarkStartupReady();

		await world.Runner.FirstRun.WaitAsync(TimeSpan.FromSeconds(10));
		await Task.Delay(_settle);

		Assert.That(world.Runner.Runs,
			Is.EqualTo(new[] { (EventTriggerOwner.ForAutomation(connected.Id), Connected, IntegrationId) }));
	}

	[Test]
	public async Task Disabling_an_integration_after_startup_runs_its_disconnected_automation()
	{
		await using var world = await World.Start();
		var automation = world.Automations.Add(Flows("integration-disconnected", IntegrationId));
		world.MarkStartupReady();
		await world.Readiness.WhenEventDispatchReady.WaitAsync(TimeSpan.FromSeconds(10));

		world.Registry.SetEnabled(IntegrationId, false);
		await world.Handler.Handle(new IntegrationStateChangedNotification(IntegrationId), CancellationToken.None);

		await world.Runner.FirstRun.WaitAsync(TimeSpan.FromSeconds(10));
		await Task.Delay(_settle);

		Assert.That(world.Runner.Runs,
			Is.EqualTo(new[] { (EventTriggerOwner.ForAutomation(automation.Id), Disconnected, IntegrationId) }));
	}

	private static string Flows(string eventId, string integrationId)
		=> JsonSerializer.Serialize(new[]
		{
			new
			{
				triggerId = "t1",
				triggerType = "onEvent",
				@event = new
				{
					providerId = "macro-deck",
					eventId,
					parameters = new[] { new { name = "integrationId", type = "string", value = integrationId } }
				},
				children = Array.Empty<object>()
			}
		});

	private sealed class World : IAsyncDisposable
	{
		private readonly IHost _host;

		private World(IHost host,
			StubAutomationCache automations,
			ThreadRecordingIntegrationRegistry registry,
			StartupReadiness readiness,
			RecordingTriggerRunner runner,
			IntegrationStateChangedEventTriggerHandler handler)
		{
			_host = host;
			Automations = automations;
			Registry = registry;
			Readiness = readiness;
			Runner = runner;
			Handler = handler;
		}

		public StubAutomationCache Automations { get; }

		public ThreadRecordingIntegrationRegistry Registry { get; }

		public StartupReadiness Readiness { get; }

		public RecordingTriggerRunner Runner { get; }

		public IntegrationStateChangedEventTriggerHandler Handler { get; }

		public static async Task<World> Start()
		{
			var automations = new StubAutomationCache();
			var index = new EventSubscriptionIndex(new StubFolderCache(), automations);
			var bus = new EventBus(index, new EventSampleStore(), Serilog.Core.Logger.None);
			var registry = new ThreadRecordingIntegrationRegistry();
			var readiness = new StartupReadiness();
			var runner = new RecordingTriggerRunner();

			var host = new HostBuilder()
				.ConfigureServices(services =>
				{
					services.AddSingleton<IEventSubscriptionIndex>(index);
					services.AddSingleton<IEventBus>(bus);
					services.AddSingleton(readiness);
					services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
					services.AddScoped<IEventTriggerRunner>(_ => runner);
					services.AddHostedService<EventDispatchBackgroundService>();
				})
				.Build();
			await host.StartAsync();

			var handler = new IntegrationStateChangedEventTriggerHandler(bus,
				registry,
				readiness,
				host.Services.GetRequiredService<IHostApplicationLifetime>());

			return new World(host, automations, registry, readiness, runner, handler);
		}

		public void MarkStartupReady()
		{
			Readiness.MarkCachesReady();
			Readiness.MarkVariablesReady();
		}

		public async ValueTask DisposeAsync()
		{
			await _host.StopAsync();
			_host.Dispose();
		}
	}

	private sealed class RecordingTriggerRunner : IEventTriggerRunner
	{
		private readonly TaskCompletionSource _firstRun = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public ConcurrentQueue<(EventTriggerOwner Owner, string EventId, object? IntegrationId)> Runs { get; } = new();

		public Task FirstRun => _firstRun.Task;

		public Task<FlowExecutionResult> Run(
			EventSubscription subscription,
			EventOccurrence occurrence,
			CancellationToken cancellationToken)
		{
			Runs.Enqueue((subscription.Owner, occurrence.EventId, occurrence.Parameters["integrationId"]));
			_firstRun.TrySetResult();
			return Task.FromResult(new FlowExecutionResult
			{
				ExecutionId = Guid.NewGuid(),
				Status = FlowExecutionStatus.Succeeded
			});
		}
	}
}
