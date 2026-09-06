using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Application.Triggers.Providers;
using Microsoft.Extensions.Hosting;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class ServerLifecycleEventBackgroundService : IHostedService
{
	private static readonly TimeSpan _shutdownDispatchWindow = TimeSpan.FromSeconds(2);

	private readonly IEventBus _bus;
	private readonly StartupReadiness _readiness;
	private readonly IHostApplicationLifetime _lifetime;

	public ServerLifecycleEventBackgroundService(
		IEventBus bus,
		StartupReadiness readiness,
		IHostApplicationLifetime lifetime)
	{
		_bus = bus;
		_readiness = readiness;
		_lifetime = lifetime;
	}

	public Task StartAsync(CancellationToken cancellationToken)
	{
		_ = PublishWhenReady();
		return Task.CompletedTask;
	}

	public async Task StopAsync(CancellationToken cancellationToken)
	{
		_bus.Publish(Occurrence(EventIds.ServerStopped));
		await Task.Delay(_shutdownDispatchWindow, cancellationToken);
	}

	private async Task PublishWhenReady()
	{
		try
		{
			await _readiness.WhenReady.WaitAsync(_lifetime.ApplicationStopping);
			_bus.Publish(Occurrence(EventIds.ServerStarted));
		}
		catch (OperationCanceledException)
		{
		}
	}

	private static EventOccurrence Occurrence(string eventId)
		=> new(EventIds.Qualify(eventId),
			new Dictionary<string, object?>(StringComparer.Ordinal) { ["version"] = HostVersion.Current });
}
