using MacroDeckHost.Application.Integrations;
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
	private readonly BuiltInIntegrationRegistration _builtInRegistration;
	private readonly ILogger _logger;

	private readonly Lock _gate = new();
	private readonly Dictionary<EventTarget, EventOccurrence?> _running = [];

	public EventDispatchBackgroundService(
		IHostApplicationLifetime lifetime,
		IEventBus bus,
		IEventSubscriptionIndex index,
		IServiceScopeFactory scopeFactory,
		StartupReadiness readiness,
		BuiltInIntegrationRegistration builtInRegistration,
		ILogger logger)
		: base(lifetime)
	{
		_bus = bus;
		_index = index;
		_scopeFactory = scopeFactory;
		_readiness = readiness;
		_builtInRegistration = builtInRegistration;
		_logger = logger.ForContext<EventDispatchBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _readiness.WhenReady.WaitAsync(stoppingToken);
		_index.Rebuild();

		// Rebuilt first so events raised during this wait queue in the bus instead of being dropped.
		await _builtInRegistration.Completed.WaitAsync(stoppingToken);
		_readiness.MarkEventDispatchReady();

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
		lock (_gate)
		{
			if (_running.TryGetValue(target, out var pending))
			{
				Hold(subscription, occurrence, pending);
				return;
			}

			_running[target] = null;
		}

		_ = Task.Run(() => RunUntilNothingIsPending(subscription, occurrence, stoppingToken), stoppingToken);
	}

	private void Hold(EventSubscription subscription, EventOccurrence occurrence, EventOccurrence? pending)
	{
		if (pending is not null)
		{
			_logger.Information("Replacing a waiting occurrence of {EventId} for event trigger {TriggerId} on {Owner}",
				occurrence.EventId,
				subscription.TriggerId,
				subscription.Owner);

			// The deepest depth is kept, or an unrelated occurrence would reset a chain that is running away.
			occurrence = occurrence with
			{
				CascadeDepth = Math.Max(occurrence.CascadeDepth, pending.CascadeDepth)
			};
		}

		_running[subscription.Target] = occurrence;
	}

	private async Task RunUntilNothingIsPending(
		EventSubscription subscription,
		EventOccurrence occurrence,
		CancellationToken stoppingToken)
	{
		var target = subscription.Target;
		var current = subscription;
		var next = occurrence;

		while (next is not null)
		{
			if (current is not null)
			{
				await RunOnce(current, next, stoppingToken);
			}

			lock (_gate)
			{
				next = stoppingToken.IsCancellationRequested ? null : _running[target];
				if (next is null)
				{
					_running.Remove(target);
				}
				else
				{
					_running[target] = null;
				}
			}

			current = next is null ? null : _index.Find(target);
		}
	}

	private async Task RunOnce(
		EventSubscription subscription,
		EventOccurrence occurrence,
		CancellationToken stoppingToken)
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
	}
}
