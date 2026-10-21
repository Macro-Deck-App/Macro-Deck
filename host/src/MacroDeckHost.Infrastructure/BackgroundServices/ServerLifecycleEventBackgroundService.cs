using MacroDeckHost.Application.Devices;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class ServerLifecycleEventBackgroundService : IHostedLifecycleService
{
	private static readonly TimeSpan _shutdownDispatchWindow = TimeSpan.FromSeconds(2);

	private readonly IEventBus _bus;
	private readonly IEventSubscriptionIndex _subscriptions;
	private readonly StartupReadiness _readiness;
	private readonly IHostApplicationLifetime _lifetime;
	private readonly DeviceConnectionTracker _connections;
	private readonly TimeProvider _timeProvider;

	public ServerLifecycleEventBackgroundService(
		IEventBus bus,
		IEventSubscriptionIndex subscriptions,
		StartupReadiness readiness,
		IHostApplicationLifetime lifetime,
		DeviceConnectionTracker connections,
		TimeProvider timeProvider)
	{
		_bus = bus;
		_subscriptions = subscriptions;
		_readiness = readiness;
		_lifetime = lifetime;
		_connections = connections;
		_timeProvider = timeProvider;
	}

	public Task StartingAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_ = PublishWhenReady();
		return Task.CompletedTask;
	}

	public Task StartedAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	public async Task StoppingAsync(CancellationToken cancellationToken)
	{
		try
		{
			var occurrence = Occurrence(EventIds.ServerStopped);
			_bus.Publish(occurrence);
			if (_subscriptions.HasSubscribers(occurrence.EventId))
			{
				await Task.Delay(_shutdownDispatchWindow, _timeProvider, cancellationToken);
			}
		}
		finally
		{
			// Server stopped flows still reach clients until here. Kestrel stops before every other hosted
			// service and waits out the shutdown timeout on open UI sockets, so they must close in this phase.
			_connections.AbortAll();
		}
	}

	public Task StopAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	public Task StoppedAsync(CancellationToken cancellationToken)
	{
		return Task.CompletedTask;
	}

	private async Task PublishWhenReady()
	{
		try
		{
			// The bus drops events nobody subscribes to, so publishing before the dispatcher has indexed the
			// automations would lose this one-shot event.
			await _readiness.WhenEventDispatchReady.WaitAsync(_lifetime.ApplicationStopping);
			_bus.Publish(Occurrence(EventIds.ServerStarted));
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static EventOccurrence Occurrence(string eventId)
	{
		return new EventOccurrence(EventIds.Qualify(eventId),
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["version"] = HostVersion.Current });
	}
}
