using System.Collections.Concurrent;
using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class EventDispatchBackgroundService : HostReadyBackgroundService
{
	private readonly IEventBus _bus;
	private readonly IEventSubscriptionIndex _index;
	private readonly IServiceScopeFactory _scopeFactory;
	private readonly StartupReadiness _readiness;
	private readonly ILogger _logger;

	private readonly ConcurrentDictionary<EventTarget, byte> _inFlight = new();

	public EventDispatchBackgroundService(
		IHostApplicationLifetime lifetime,
		IEventBus bus,
		IEventSubscriptionIndex index,
		IServiceScopeFactory scopeFactory,
		StartupReadiness readiness,
		ILogger logger)
		: base(lifetime)
	{
		_bus = bus;
		_index = index;
		_scopeFactory = scopeFactory;
		_readiness = readiness;
		_logger = logger.ForContext<EventDispatchBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _readiness.WhenReady.WaitAsync(stoppingToken);
		_index.Rebuild();

		var reader = _bus.Reader;
		while (await reader.WaitToReadAsync(stoppingToken))
		{
			while (reader.TryRead(out var occurrence))
			{
				foreach (var subscription in Resolve(occurrence))
				{
					Dispatch(subscription, occurrence, stoppingToken);
				}
			}
		}
	}

	private IReadOnlyList<EventSubscription> Resolve(EventOccurrence occurrence)
	{
		if (occurrence.Target is not { } target)
		{
			return _index.Find(occurrence.EventId);
		}

		var subscription = _index.Find(target);
		return subscription is null ? [] : [subscription];
	}

	private void Dispatch(EventSubscription subscription, EventOccurrence occurrence, CancellationToken stoppingToken)
	{
		if (EventSubscriptionMatcher.QuickReject(subscription, occurrence))
		{
			return;
		}

		var target = subscription.Target;
		if (!_inFlight.TryAdd(target, 0))
		{
			_logger.Debug("Skipping event trigger {TriggerId} on {Owner}: previous run still in progress",
				subscription.TriggerId,
				subscription.Owner);
			return;
		}

		_ = Task.Run(async () =>
			{
				try
				{
					using var scope = _scopeFactory.CreateScope();
					var runner = scope.ServiceProvider.GetRequiredService<IEventTriggerRunner>();
					await runner.Run(subscription, occurrence, stoppingToken);
				}
				catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
				{
				}
				catch (Exception ex)
				{
					_logger.Error(ex,
						"Event trigger {TriggerId} on {Owner} failed for {EventId}",
						subscription.TriggerId,
						subscription.Owner,
						occurrence.EventId);
				}
				finally
				{
					_inFlight.TryRemove(target, out _);
				}
			},
			stoppingToken);
	}
}
