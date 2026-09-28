using System.Collections.Concurrent;
using System.Text.Json;
using System.Threading.Channels;
using MacroDeckHost.Application.Actions;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Auth;
using MacroDeckHost.Tests.UnitTests.TestSupport;
using MacroDeckHost.Tests.UnitTests.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
public sealed class ServerLifecycleEventBackgroundServiceTests
{
	private const string ServerStarted = "macro-deck::server-started";
	private const string ServerStopped = "macro-deck::server-stopped";

	[Test]
	public async Task An_enabled_server_started_automation_runs_once_when_the_host_has_started()
	{
		var automations = new StubAutomationCache();
		var automation = automations.Add(ServerStartedFlows());
		var index = new EventSubscriptionIndex(new StubFolderCache(), automations);
		var runner = new RecordingTriggerRunner();
		var readiness = new StartupReadiness();
		var bus = new EventBus(index, new EventSampleStore(), Serilog.Core.Logger.None);

		using var host = new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddSingleton<IEventSubscriptionIndex>(index);
				services.AddSingleton<IEventBus>(bus);
				services.AddSingleton(readiness);
				services.AddSingleton(TimeProvider.System);
				services.AddSingleton<Serilog.ILogger>(Serilog.Core.Logger.None);
				services.AddSingleton(new DeviceConnectionTracker(bus,
					TimeProvider.System,
					new DeckClientTracker(Serilog.Core.Logger.None)));
				services.AddScoped<IEventTriggerRunner>(_ => runner);
				services.AddHostedService<EventDispatchBackgroundService>();
				services.AddHostedService<ServerLifecycleEventBackgroundService>();
			})
			.Build();
		await host.StartAsync();
		readiness.MarkCachesReady();
		readiness.MarkVariablesReady();

		await runner.FirstRun.WaitAsync(TimeSpan.FromSeconds(10));
		await host.StopAsync();

		Assert.That(runner.Runs.Select(run => (run.Owner, run.EventId)),
			Is.EqualTo(new[] { (EventTriggerOwner.ForAutomation(automation.Id), ServerStarted) }));
	}

	private static string ServerStartedFlows()
		=> JsonSerializer.Serialize(new[]
		{
			new
			{
				triggerId = "t1",
				triggerType = "onEvent",
				@event = new { providerId = "macro-deck", eventId = "server-started", parameters = Array.Empty<object>() },
				children = Array.Empty<object>()
			}
		});

	[Test]
	public async Task Server_stopped_flows_can_still_reach_clients_and_clients_are_closed_before_the_web_server_stops()
	{
		var time = new ManualTimeProvider();
		var bus = new ConnectionAwareEventBus();
		var tracker = new DeviceConnectionTracker(bus, time, new DeckClientTracker(Serilog.Core.Logger.None));
		var clientClosed = false;
		tracker.Attach("companion", Guid.NewGuid(), () => clientClosed = true);
		bus.ClientClosed = () => clientClosed;
		var webServer = new WebServerProbe(() => clientClosed);

		using var host = new HostBuilder()
			.ConfigureServices(services =>
			{
				services.AddSingleton<IEventBus>(bus);
				services.AddSingleton<TimeProvider>(time);
				services.AddSingleton(tracker);
				services.AddSingleton(new StartupReadiness());
				services.AddHostedService<ServerLifecycleEventBackgroundService>();
				services.AddHostedService(_ => webServer);
			})
			.Build();
		await host.StartAsync();
		var shutdown = host.WaitForShutdownAsync();
		var scheduled = time.ScheduledCount;

		host.Services.GetRequiredService<IHostApplicationLifetime>().StopApplication();
		await time.WaitForScheduleAsync(scheduled).WaitAsync(TimeSpan.FromSeconds(10));
		var closedBeforeDispatchWindowEnded = clientClosed;
		time.Advance(TimeSpan.FromSeconds(2));
		await shutdown.WaitAsync(TimeSpan.FromSeconds(10));

		Assert.Multiple(() =>
		{
			Assert.That(bus.PublishedWhileClientConnected, Is.EqualTo(new[] { ServerStopped }));
			Assert.That(closedBeforeDispatchWindowEnded, Is.False);
			Assert.That(webServer.ClientClosedWhenStopping, Is.True);
		});
	}

	private sealed class RecordingTriggerRunner : IEventTriggerRunner
	{
		private readonly TaskCompletionSource _firstRun = new(TaskCreationOptions.RunContinuationsAsynchronously);

		public ConcurrentQueue<(EventTriggerOwner Owner, string EventId)> Runs { get; } = new();

		public Task FirstRun => _firstRun.Task;

		public Task<FlowExecutionResult> Run(
			EventSubscription subscription,
			EventOccurrence occurrence,
			CancellationToken cancellationToken)
		{
			Runs.Enqueue((subscription.Owner, occurrence.EventId));
			_firstRun.TrySetResult();
			return Task.FromResult(new FlowExecutionResult { ExecutionId = Guid.NewGuid(), Status = FlowExecutionStatus.Succeeded });
		}
	}

	private sealed class ConnectionAwareEventBus : IEventBus
	{
		private readonly Channel<EventOccurrence> _channel = Channel.CreateUnbounded<EventOccurrence>();

		public Func<bool> ClientClosed { get; set; } = () => false;

		public List<string> PublishedWhileClientConnected { get; } = [];

		public ChannelReader<EventOccurrence> Reader => _channel.Reader;

		public void Publish(EventOccurrence occurrence)
		{
			if (!ClientClosed())
			{
				PublishedWhileClientConnected.Add(occurrence.EventId);
			}
		}
	}

	private sealed class WebServerProbe(Func<bool> clientClosed) : IHostedService
	{
		public bool? ClientClosedWhenStopping { get; private set; }

		public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

		public Task StopAsync(CancellationToken cancellationToken)
		{
			ClientClosedWhenStopping = clientClosed();
			return Task.CompletedTask;
		}
	}
}
