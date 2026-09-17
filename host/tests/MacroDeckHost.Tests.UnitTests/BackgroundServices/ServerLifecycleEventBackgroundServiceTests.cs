using System.Threading.Channels;
using MacroDeckHost.Application.Deck;
using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Infrastructure.BackgroundServices;
using MacroDeckHost.Tests.UnitTests.Auth;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Tests.UnitTests.BackgroundServices;

[TestFixture]
public sealed class ServerLifecycleEventBackgroundServiceTests
{
	private const string ServerStopped = "macro-deck::server-stopped";

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
