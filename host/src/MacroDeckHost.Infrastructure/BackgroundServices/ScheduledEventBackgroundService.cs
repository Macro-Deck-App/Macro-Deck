using MacroDeckHost.Application.Services;
using MacroDeckHost.Application.Triggers;
using MacroDeckHost.Infrastructure.Triggers;
using Microsoft.Extensions.Hosting;
using ILogger = Serilog.ILogger;

namespace MacroDeckHost.Infrastructure.BackgroundServices;

public sealed class ScheduledEventBackgroundService : HostReadyBackgroundService
{
	private static readonly TimeSpan _maximumSleep = TimeSpan.FromHours(1);

	private readonly IEventBus _bus;
	private readonly IEventSubscriptionIndex _index;
	private readonly StartupReadiness _readiness;
	private readonly TimeProvider _time;
	private readonly ILogger _logger;

	private readonly SemaphoreSlim _replan = new(0, 1);

	public ScheduledEventBackgroundService(
		IHostApplicationLifetime lifetime,
		IEventBus bus,
		IEventSubscriptionIndex index,
		StartupReadiness readiness,
		TimeProvider time,
		ILogger logger)
		: base(lifetime)
	{
		_bus = bus;
		_index = index;
		_readiness = readiness;
		_time = time;
		_logger = logger.ForContext<ScheduledEventBackgroundService>();
	}

	protected override async Task ExecuteWhenReady(CancellationToken stoppingToken)
	{
		await _readiness.WhenReady.WaitAsync(stoppingToken);

		_index.Changed += OnIndexChanged;
		try
		{
			var due = new Dictionary<EventTarget, DateTimeOffset>();

			while (!stoppingToken.IsCancellationRequested)
			{
				var now = _time.GetUtcNow();
				var next = Plan(due, now);

				await FireDue(due, now, stoppingToken);

				var sleep = next is null
					? _maximumSleep
					: Clamp(next.Value - _time.GetUtcNow());

				await _replan.WaitAsync(sleep, stoppingToken);
			}
		}
		catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
		{
		}
		finally
		{
			_index.Changed -= OnIndexChanged;
		}
	}

	private DateTimeOffset? Plan(Dictionary<EventTarget, DateTimeOffset> due, DateTimeOffset now)
	{
		var timeZone = TimeZoneInfo.Local;
		var subscriptions = _index.FindByProvider(TimeEventProvider.ProviderIdValue);
		var live = new HashSet<EventTarget>();

		foreach (var subscription in subscriptions)
		{
			var target = subscription.Target;
			live.Add(target);

			if (due.ContainsKey(target))
			{
				continue;
			}

			var schedule = TriggerSchedule.TryCreate(subscription);
			if (schedule is null)
			{
				continue;
			}

			var next = schedule.NextOccurrence(now, timeZone);
			if (next is not null)
			{
				due[target] = next.Value;
			}
		}

		foreach (var stale in due.Keys.Where(target => !live.Contains(target)).ToList())
		{
			due.Remove(stale);
		}

		return due.Count == 0 ? null : due.Values.Min();
	}

	private Task FireDue(
		Dictionary<EventTarget, DateTimeOffset> due,
		DateTimeOffset now,
		CancellationToken cancellationToken)
	{
		var timeZone = TimeZoneInfo.Local;

		foreach (var (target, scheduledFor) in due.Where(entry => entry.Value <= now).ToList())
		{
			var subscription = _index.Find(target);
			var schedule = subscription is null ? null : TriggerSchedule.TryCreate(subscription);
			if (subscription is null || schedule is null)
			{
				due.Remove(target);
				continue;
			}

			_bus.Publish(new EventOccurrence(subscription.EventId,
				new Dictionary<string, object?>(StringComparer.Ordinal)
				{
					["firedAt"] = now.ToLocalTime().ToString("O"),
					["scheduledFor"] = scheduledFor.ToLocalTime().ToString("O")
				},
				target));

			var next = schedule.NextOccurrence(now, timeZone);
			if (next is null)
			{
				due.Remove(target);
			}
			else
			{
				due[target] = next.Value;
			}
		}

		return Task.CompletedTask;
	}

	private static TimeSpan Clamp(TimeSpan sleep)
		=> sleep < TimeSpan.Zero ? TimeSpan.Zero : sleep > _maximumSleep ? _maximumSleep : sleep;

	private void OnIndexChanged()
	{
		try
		{
			_replan.Release();
		}
		catch (SemaphoreFullException)
		{
		}
	}
}
